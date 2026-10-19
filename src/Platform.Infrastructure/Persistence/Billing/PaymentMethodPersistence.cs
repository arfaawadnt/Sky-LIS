using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Billing;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Billing;

namespace Platform.Infrastructure.Persistence.Billing;

/// <summary>
/// <c>app.payment_methods</c> (change request 2026-10-19, P-C10): key <c>(tenant_id, id)</c> by convention, the code
/// unique per tenant, the kind as a string with its CHECK (convention), RLS declared by the integrator's migration.
/// Payments and refunds carry the code (<c>method_code</c>) without a foreign key: a definition may be deactivated
/// while history keeps its code, and the lists join the names when the code is still known.
/// </summary>
internal sealed class PaymentMethodDefinitionConfiguration : IEntityTypeConfiguration<PaymentMethodDefinition>
{
    public void Configure(EntityTypeBuilder<PaymentMethodDefinition> builder)
    {
        builder.ToTable("payment_methods", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint("ck_payment_methods_sequence", $"sequence >= 0 AND sequence <= {PaymentMethodDefinition.MaxSequence}"));

        builder.Property(method => method.Code).HasMaxLength(PaymentMethodDefinition.MaxCodeLength).IsRequired();
        builder.HasIndex(method => new { method.TenantId, method.Code }).IsUnique();

        builder.OwnsOne(method => method.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(PaymentMethodDefinition.MaxNameLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(PaymentMethodDefinition.MaxNameLength).IsRequired();
        });
        builder.Navigation(method => method.Name).IsRequired();

        builder.Property(method => method.Kind).IsRequired();
        builder.Property(method => method.RequiresReference).IsRequired();
        builder.Property(method => method.IsActive).IsRequired();
        builder.Property(method => method.Sequence).IsRequired();
        builder.HasIndex(method => new { method.TenantId, method.IsActive, method.Sequence });
    }
}

internal sealed class PaymentMethodRepository(AppDbContext context) : Repository<PaymentMethodDefinition>(context), IPaymentMethodRepository
{
    private const string LikeEscape = "\\";

    private static readonly SortColumns<PaymentMethodDefinition> Sorts = new SortColumns<PaymentMethodDefinition>(BillingSortKeys.PaymentMethods)
        .Column("code", method => method.Code)
        .Column("nameEn", method => method.Name.En)
        .Column("nameAr", method => method.Name.Ar)
        .Column("kind", method => method.Kind)
        .Column("sequence", method => method.Sequence)
        .ThenBy(method => method.Sequence)
        .ThenBy(method => method.Code)
        .ThenBy(method => method.Id);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(method => method.Code == cleaned, cancellationToken);
    }

    public Task<PaymentMethodDefinition?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AsNoTracking().FirstOrDefaultAsync(method => method.Code == cleaned && method.IsActive, cancellationToken);
    }

    public Task<bool> OtherActiveCashExistsAsync(Guid excludedId, CancellationToken cancellationToken) =>
        Set.AnyAsync(method => method.Id != excludedId && method.IsActive && method.Kind == PaymentMethod.Cash, cancellationToken);

    public async Task<IReadOnlyCollection<string>> ExistingCodesAsync(CancellationToken cancellationToken) =>
        await Set.AsNoTracking().Select(method => method.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Paged<PaymentMethodListItem>> ListAsync(string? search, bool? isActive, ListSort sort, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = ContainsPattern(search);
            query = query.Where(method => EF.Functions.ILike(method.Code, pattern, LikeEscape) || EF.Functions.ILike(method.Name.En, pattern, LikeEscape) || EF.Functions.ILike(method.Name.Ar, pattern, LikeEscape));
        }

        if (isActive is { } active)
        {
            query = query.Where(method => method.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await Sorts.Apply(query, sort, methods => methods.OrderBy(method => method.Sequence).ThenBy(method => method.Code))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(method => new PaymentMethodListItem(method.Id, method.Code, method.Name.En, method.Name.Ar, method.Kind.ToString(), method.RequiresReference, method.IsActive, method.Sequence))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<PaymentMethodListItem>(items, page.Page, page.PageSize, total);
    }

    public Task<PaymentMethodListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking()
            .Where(method => method.Id == id)
            .Select(method => new PaymentMethodListItem(method.Id, method.Code, method.Name.En, method.Name.Ar, method.Kind.ToString(), method.RequiresReference, method.IsActive, method.Sequence))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentMethodOption>> OptionsAsync(CancellationToken cancellationToken) =>
        await Set.AsNoTracking()
            .Where(method => method.IsActive)
            .OrderBy(method => method.Sequence).ThenBy(method => method.Code)
            .Select(method => new PaymentMethodOption(method.Id, method.Code, method.Name.En, method.Name.Ar, method.Kind.ToString(), method.RequiresReference))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary><c>%text%</c> with the LIKE wildcards of the text escaped (a typed <c>_</c> or <c>%</c> is literal).</summary>
    private static string ContainsPattern(string search)
    {
        var escaped = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return "%" + escaped + "%";
    }
}
