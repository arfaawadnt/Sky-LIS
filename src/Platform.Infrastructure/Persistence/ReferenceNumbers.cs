using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence;

/// <summary>One counter row per tenant, kind and year (<c>app.reference_counters</c>). Keyed without an id; the tenant query filter and RLS still apply.</summary>
public sealed class ReferenceCounter : ITenantScoped
{
    public const int MaxKindLength = 40;

    private ReferenceCounter()
    {
        Kind = string.Empty;
    }

    public Guid TenantId { get; private set; }

    public string Kind { get; private set; }

    public int Year { get; private set; }

    public long LastValue { get; private set; }
}

internal sealed class ReferenceCounterConfiguration : IEntityTypeConfiguration<ReferenceCounter>
{
    public void Configure(EntityTypeBuilder<ReferenceCounter> builder)
    {
        builder.ToTable("reference_counters", AppDbContext.AppSchema);
        builder.HasKey(counter => new { counter.TenantId, counter.Kind, counter.Year });
        builder.Property(counter => counter.Kind).HasMaxLength(ReferenceCounter.MaxKindLength);
        builder.Property(counter => counter.LastValue).IsRequired();
    }
}

/// <summary>
/// Atomic per-tenant numbering (pitfall P-12): a single <c>INSERT … ON CONFLICT DO UPDATE … RETURNING</c> on the
/// counter row, which PostgreSQL serialises on the row lock, so 50 parallel callers get 50 distinct values. Gaps
/// are allowed; reuse is impossible. The counter lives in the current tenant: an unresolved tenant is refused.
/// </summary>
public sealed class ReferenceNumbers(AppDbContext context, ITenantContextAccessor tenant) : IReferenceNumbers
{
    /// <summary>The year of the counters that never restart.</summary>
    public const int UndatedYear = 0;

    private const string NextSql = """
        WITH counter AS (
            INSERT INTO app.reference_counters (tenant_id, kind, year, last_value)
            VALUES ({0}, {1}, {2}, 1)
            ON CONFLICT (tenant_id, kind, year) DO UPDATE SET last_value = app.reference_counters.last_value + 1
            RETURNING last_value
        )
        SELECT last_value AS "Value" FROM counter
        """;

    public async Task<string> NextAsync(string kind, string prefix, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var tenantId = tenant.CurrentTenantId
            ?? throw new DomainException(ProblemCodes.TenantUnresolved, "No tenant is resolved for this scope; a reference number cannot be issued.", DomainFault.Refused);

        var values = await context.Database.SqlQueryRaw<long>(NextSql, tenantId, kind, nowUtc.Year).ToListAsync(cancellationToken).ConfigureAwait(false);
        var next = values.Single();
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{nowUtc.Year:D4}-{next:D5}");
    }

    /// <summary>
    /// The never-restarting counter of short codes (LIS-CR-007 C-09): the same row-locked upsert on the year-0 row of the
    /// kind (no calendar year is 0, so it cannot meet a yearly counter of the same kind).
    /// </summary>
    public async Task<string> NextShortAsync(string kind, string prefix, int width, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var tenantId = tenant.CurrentTenantId
            ?? throw new DomainException(ProblemCodes.TenantUnresolved, "No tenant is resolved for this scope; a reference number cannot be issued.", DomainFault.Refused);

        var values = await context.Database.SqlQueryRaw<long>(NextSql, tenantId, kind, UndatedYear).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ReferenceNumberFormat.Short(prefix, values.Single(), width);
    }
}
