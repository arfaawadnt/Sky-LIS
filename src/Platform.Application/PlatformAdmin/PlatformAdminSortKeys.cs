using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.PlatformAdmin;

/// <summary>
/// The sortable columns of the Super Admin registers that page on the server (validation v0.5.0 B-03): clients (SA-03),
/// countries (SA-10), governorates (SA-11), notification templates (SA-20) and the integration library (SA-15 to SA-18).
/// The validators refuse any other key; the repositories map each key through <see cref="SortColumns{T}"/>.
/// </summary>
public static class PlatformAdminSortKeys
{
    public static readonly SortKeys Clients = new("nameEn", "nameAr", "planCode", "walletBalance", "createdAtUtc");
    public static readonly SortKeys Countries = new("nameEn", "nameAr", "isoCode");
    public static readonly SortKeys Governorates = new("nameEn", "nameAr", "countryEn", "countryAr");
    public static readonly SortKeys NotificationTemplates = new("code");
    public static readonly SortKeys AnalyserModels = new("vendor", "model");
    public static readonly SortKeys PcrKits = new("name", "analyserModel");
    public static readonly SortKeys CodeSystems = new("name", "version");
    public static readonly SortKeys RuleTemplates = new("name", "clinicalGroupName");
}
