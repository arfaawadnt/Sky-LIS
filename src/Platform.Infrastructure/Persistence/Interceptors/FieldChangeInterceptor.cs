using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Platform.Application.Abstractions.Observability;
using Platform.Application.Abstractions.Security;
using Platform.Infrastructure.Persistence.Audit;
using Platform.Infrastructure.Persistence.Conventions;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Field-level history (01 §4.8, 03 §8): for every tracked record the commit touches, one <c>audit.field_changes</c>
/// row with the before/after of each changed field, written in the same transaction. Tracked: aggregates, entities
/// and rows marked <see cref="IFieldChangeTracked"/>; never sessions, keys, nonces, one-time codes, counters, outbox
/// or audit rows. Skipped: the row version, the audit stamps, key columns and credential material (hashes, secrets,
/// stamps). Owned values (bilingual names, approvals) are folded into their owner as <c>Navigation.Property</c>.
/// A record of a global (tenant-less) aggregate is recorded under the tenant of the scope when one is resolved.
/// Runs after the tenant and audit stamps and before the trail and the outbox (InterceptorOrderTests).
/// </summary>
public sealed class FieldChangeInterceptor(ICurrentUser currentUser, ITenantContextAccessor tenant, IRequestContext request, IClock clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal)
    {
        XminConcurrencyConvention.PropertyName,
        nameof(AggregateRoot.CreatedAtUtc), nameof(AggregateRoot.CreatedByUserId), nameof(AggregateRoot.CreatedByDisplayName),
        nameof(AggregateRoot.ModifiedAtUtc), nameof(AggregateRoot.ModifiedByUserId), nameof(AggregateRoot.ModifiedByDisplayName),
        nameof(ITenantScoped.TenantId),
    };

    private static readonly string[] SensitiveFragments = ["PasswordHash", "Secret", "SecurityStamp", "TokenHash"];
    private static readonly string[] ReasonProperties = ["Reason", "DecisionReason", "Comment"];
    private static readonly string[] ExcludedSchemas = [AppDbContext.AuditSchema, AppDbContext.OpsSchema];
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> ReasonPropertyCache = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>The rule: aggregates and entities (anything deriving from <c>Entity&lt;T&gt;</c>) plus marked rows, outside the audit and ops schemas and outside the credential tables.</summary>
    public static bool IsTracked(IEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (entityType.IsOwned() || ExcludedSchemas.Contains(entityType.GetSchema() ?? string.Empty, StringComparer.Ordinal))
        {
            return false;
        }

        var clrType = entityType.ClrType;
        if (clrType == typeof(Domain.Identity.RefreshSession))
        {
            return false;
        }

        return typeof(IFieldChangeTracked).IsAssignableFrom(clrType) || DerivesFromEntity(clrType);
    }

    public static bool IsSkippedProperty(string propertyName) =>
        SkippedProperties.Contains(propertyName) || SensitiveFragments.Any(fragment => propertyName.Contains(fragment, StringComparison.Ordinal));

    private void Record(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entries = context.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var drafts = new Dictionary<(Type Type, string Id), Draft>();
        foreach (var entry in entries)
        {
            if (entry.Metadata.IsOwned())
            {
                FoldOwned(entry, drafts);
                continue;
            }

            if (!IsTracked(entry.Metadata))
            {
                continue;
            }

            var tenantId = TenantOf(entry);
            if (tenantId is null)
            {
                continue;
            }

            var draft = GetOrAdd(drafts, entry.Metadata.ClrType, EntityIdOf(entry), tenantId.Value);
            draft.Action = entry.State switch
            {
                EntityState.Added => FieldChangeAction.Created,
                EntityState.Deleted => FieldChangeAction.Deleted,
                _ => draft.Action ?? FieldChangeAction.Modified,
            };

            foreach (var property in entry.Properties)
            {
                if (property.Metadata.IsPrimaryKey() || property.Metadata.IsShadowProperty() || IsSkippedProperty(property.Metadata.Name))
                {
                    continue;
                }

                AddChange(draft, property.Metadata.Name, property, entry.State);
            }

            if (entry.Entity is IDomainEventSource source)
            {
                draft.Reason ??= ReasonOf(source);
            }
        }

        var now = clock.UtcNow;
        var rows = drafts.Values
            .Where(draft => draft.Changes.Count > 0 || draft.Action is FieldChangeAction.Created or FieldChangeAction.Deleted)
            .Select(draft => new FieldChange(
                draft.TenantId,
                draft.Type.Name,
                draft.Id,
                draft.Action ?? FieldChangeAction.Modified,
                currentUser.UserId,
                currentUser.DisplayName,
                now,
                request.CorrelationId,
                JsonSerializer.Serialize(draft.Changes.Values.Select(change => new { change.Field, change.Before, change.After }), Json),
                draft.Reason))
            .ToList();

        if (rows.Count > 0)
        {
            context.Set<FieldChange>().AddRange(rows);
        }
    }

    /// <summary>An owned value that shares its owner's table is recorded on the owner; a replaced owned instance shows as one Modified change per field.</summary>
    private void FoldOwned(EntityEntry entry, Dictionary<(Type, string), Draft> drafts)
    {
        var ownership = entry.Metadata.FindOwnership();
        if (ownership is null)
        {
            return;
        }

        var owner = ownership.PrincipalEntityType;
        if (owner.IsOwned() || !IsTracked(owner))
        {
            return;
        }

        var ownerKey = owner.FindPrimaryKey();
        if (ownerKey is null)
        {
            return;
        }

        // The dependent's FK properties hold the owner's key values, tenant first for tenant tables.
        Guid? tenantId = null;
        string? ownerId = null;
        for (var i = 0; i < ownership.Properties.Count; i++)
        {
            var value = entry.Property(ownership.Properties[i].Name).CurrentValue ?? entry.Property(ownership.Properties[i].Name).OriginalValue;
            if (ownerKey.Properties[i].Name == nameof(ITenantScoped.TenantId) && value is Guid tenantValue)
            {
                tenantId = tenantValue;
            }
            else
            {
                ownerId = ownerId is null ? Format(value) : ownerId + ":" + Format(value);
            }
        }

        tenantId ??= typeof(ITenantScoped).IsAssignableFrom(owner.ClrType) ? null : tenant.CurrentTenantId;
        if (tenantId is null || ownerId is null)
        {
            return;
        }

        var navigation = ownership.PrincipalToDependent?.Name ?? entry.Metadata.ShortName();
        var draft = GetOrAdd(drafts, owner.ClrType, ownerId, tenantId.Value);
        draft.Action ??= FieldChangeAction.Modified;

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsPrimaryKey() || property.Metadata.IsShadowProperty() || ownership.Properties.Contains(property.Metadata) || IsSkippedProperty(property.Metadata.Name))
            {
                continue;
            }

            AddChange(draft, navigation + "." + property.Metadata.Name, property, entry.State);
        }
    }

    private static void AddChange(Draft draft, string field, PropertyEntry property, EntityState state)
    {
        switch (state)
        {
            case EntityState.Added:
                var after = Format(property.CurrentValue);
                if (after is not null || draft.Changes.ContainsKey(field))
                {
                    Merge(draft, field, null, after, replaceAfter: true);
                }

                break;

            case EntityState.Deleted:
                var before = Format(property.OriginalValue);
                if (before is not null || draft.Changes.ContainsKey(field))
                {
                    Merge(draft, field, before, null, replaceAfter: false);
                }

                break;

            case EntityState.Modified:
                if (property.IsModified && !property.Metadata.GetValueComparer().Equals(property.OriginalValue, property.CurrentValue))
                {
                    draft.Changes[field] = new Change(field, Format(property.OriginalValue), Format(property.CurrentValue));
                }

                break;
        }
    }

    /// <summary>A replaced owned instance arrives as one Deleted and one Added entry: their halves meet here as a single before/after.</summary>
    private static void Merge(Draft draft, string field, string? before, string? after, bool replaceAfter)
    {
        if (draft.Changes.TryGetValue(field, out var existing))
        {
            var merged = replaceAfter ? existing with { After = after } : existing with { Before = before };
            if (merged.Before == merged.After)
            {
                draft.Changes.Remove(field);
            }
            else
            {
                draft.Changes[field] = merged;
            }

            return;
        }

        draft.Changes[field] = new Change(field, before, after);
    }

    private Guid? TenantOf(EntityEntry entry) =>
        entry.Entity is ITenantScoped { TenantId: var id } && id != Guid.Empty ? id : tenant.CurrentTenantId;

    private static string EntityIdOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return string.Empty;
        }

        var parts = key.Properties
            .Where(property => property.Name != nameof(ITenantScoped.TenantId))
            .Select(property => Format(entry.Property(property.Name).CurrentValue ?? entry.Property(property.Name).OriginalValue) ?? string.Empty);
        return string.Join(':', parts);
    }

    private static Draft GetOrAdd(Dictionary<(Type, string), Draft> drafts, Type type, string id, Guid tenantId)
    {
        if (!drafts.TryGetValue((type, id), out var draft))
        {
            draft = new Draft(type, id, tenantId);
            drafts[(type, id)] = draft;
        }

        return draft;
    }

    private static string? ReasonOf(IDomainEventSource source)
    {
        foreach (var domainEvent in source.DomainEvents)
        {
            var property = ReasonPropertyCache.GetOrAdd(domainEvent.GetType(), static type =>
                ReasonProperties.Select(name => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)).FirstOrDefault(candidate => candidate?.PropertyType == typeof(string)));
            if (property?.GetValue(domainEvent) is string { Length: > 0 } reason)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>Text form of a stored value; binary content is never copied into the history.</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTimeOffset moment => moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
        Guid id => id.ToString("D"),
        byte[] => "<binary>",
        IPAddress address => address.ToString(),
        Enum member => member.ToString(),
        IEnumerable<Guid> ids => string.Join(',', ids.Select(id => id.ToString("D"))),
        IEnumerable<string> texts => string.Join(',', texts),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable sequence => string.Join(',', sequence.Cast<object?>().Select(item => item?.ToString())),
        _ => value.ToString(),
    };

    private static bool DerivesFromEntity(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Change(string Field, string? Before, string? After);

    private sealed class Draft(Type type, string id, Guid tenantId)
    {
        public Type Type { get; } = type;

        public string Id { get; } = id;

        public Guid TenantId { get; } = tenantId;

        public FieldChangeAction? Action { get; set; }

        public string? Reason { get; set; }

        public Dictionary<string, Change> Changes { get; } = new(StringComparer.Ordinal);
    }
}
