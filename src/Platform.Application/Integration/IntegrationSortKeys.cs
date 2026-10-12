using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Integration;

/// <summary>
/// The sortable columns of the integration registers (CP-60 analysers, CP-61 site agents, CP-62 PCR kits; validation
/// v0.5.0 B-03): the repositories order the whole filtered list before paging through <see cref="SortColumns{T}"/>.
/// </summary>
public static class IntegrationSortKeys
{
    public static readonly SortKeys Analysers = new("code", "nameEn", "nameAr");
    public static readonly SortKeys SiteAgents = new("name");
    public static readonly SortKeys PcrKits = new("name");
}
