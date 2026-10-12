using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Treasury;

/// <summary>The sortable columns of the treasury catalogue (CP-28, validation v0.5.0 B-03); see <see cref="SortColumns{T}"/>.</summary>
public static class TreasurySortKeys
{
    public static readonly SortKeys Treasuries = new("code", "nameEn", "nameAr");
}
