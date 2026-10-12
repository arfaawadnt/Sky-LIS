using Platform.Application.Abstractions.Persistence;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>The filter of the simple catalogue-A registers: text search (code and names, ignoring case) and the active flag.</summary>
/// <remarks><see cref="Sort"/> is applied on the server before paging (validation v0.5.0 B-03; keys in <see cref="CatalogueSortKeys"/>).</remarks>
public sealed record CatalogueAListFilter(string? Search, bool? IsActive, ListSort Sort = default);

public sealed record ServiceListFilter(string? Search, ServiceType? Type, Guid? ClinicalGroupId, bool? IsActive, ListSort Sort = default);

public sealed record LabTestListFilter(string? Search, LabTestType? Type, Guid? ClinicalGroupId, bool? IsActive, ListSort Sort = default);

/// <summary>What a service member points at, as the member rules need it (CAT-027, CAT-026).</summary>
public sealed record ServiceMemberState(Guid Id, ServiceType Type, bool IsActive);

/// <summary>What a test member points at (CAT-027, CAT-036).</summary>
public sealed record LabTestMemberState(Guid Id, LabTestType Type, bool IsActive);

/// <summary>One test in the formula graph (INV-CAT-009): its code, activity and the codes its formula reads.</summary>
public sealed record LabTestFormulaNode(Guid Id, string Code, bool IsActive, IReadOnlyList<string> References);

/// <summary>Bounds shared by the module's registers.</summary>
public static class CatalogueALimits
{
    /// <summary>An export returns at most this many rows (intake §14).</summary>
    public const int MaxExportRows = 10_000;

    /// <summary>Lookups for other modules return at most this many rows per page.</summary>
    public const int MaxLookupPageSize = 50;
}
