using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Pricing;

/// <summary>
/// The sortable columns of the payer and base-list registers (CP-32, CP-33; validation v0.5.0 B-03): the validators
/// refuse any other key and the repositories map each key to its column through <see cref="SortColumns{T}"/>.
/// </summary>
public static class PricingSortKeys
{
    public static readonly SortKeys Payers = new("nameEn", "nameAr", "searchKey", "type");
    public static readonly SortKeys BasePriceLists = new("code", "nameEn", "nameAr", "year");
}
