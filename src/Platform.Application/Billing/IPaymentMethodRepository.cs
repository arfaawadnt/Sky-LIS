using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Billing;

namespace Platform.Application.Billing;

/// <summary>The sortable columns of the payment methods set-up list (CP-30b).</summary>
public static class BillingSortKeys
{
    public static readonly SortKeys PaymentMethods = new("code", "nameEn", "nameAr", "kind", "sequence");
}

/// <summary>
/// Write side and projections of the payment method definitions (change request 2026-10-19, P-C10). Every read is
/// bounded to the current tenant (query filter + RLS).
/// </summary>
public interface IPaymentMethodRepository : IRepository<PaymentMethodDefinition>
{
    /// <summary>A definition with this (cleaned, upper-case) code exists, active or not (INV: unique per tenant).</summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>The active definition with this code (untracked: a movement only reads its kind and reference rule), or <see langword="null"/>.</summary>
    Task<PaymentMethodDefinition?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>INV-BIL-010: another active definition of kind Cash exists besides <paramref name="excludedId"/>.</summary>
    Task<bool> OtherActiveCashExistsAsync(Guid excludedId, CancellationToken cancellationToken);

    /// <summary>The codes already defined in the tenant (the defaults add only the missing ones).</summary>
    Task<IReadOnlyCollection<string>> ExistingCodesAsync(CancellationToken cancellationToken);

    /// <summary><paramref name="search"/> (trimmed, or null) matches the code and both names; <paramref name="sort"/> orders before paging (<see cref="BillingSortKeys.PaymentMethods"/>).</summary>
    Task<Paged<PaymentMethodListItem>> ListAsync(string? search, bool? isActive, ListSort sort, PageRequest page, CancellationToken cancellationToken);

    Task<PaymentMethodListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The active definitions in sequence order, then code (the pick lists of the cash desk and the registration page).</summary>
    Task<IReadOnlyList<PaymentMethodOption>> OptionsAsync(CancellationToken cancellationToken);
}
