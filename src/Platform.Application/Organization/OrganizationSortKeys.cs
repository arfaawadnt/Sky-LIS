using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Organization;

/// <summary>The sortable columns of the branches, users and roles registers (validation v0.5.0 B-03); see <see cref="SortColumns{T}"/>.</summary>
public static class OrganizationSortKeys
{
    public static readonly SortKeys Branches = new("code", "nameEn", "nameAr");
    public static readonly SortKeys Users = new("username", "nameEn", "nameAr");
    public static readonly SortKeys Roles = new("nameEn", "nameAr");
}
