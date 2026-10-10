using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>Write side and registers of <see cref="LabTest"/> (the aggregate loads with bands, list values and paragraphs).</summary>
public interface ILabTestRepository : IRepository<LabTest>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, LabTestMemberState>> MemberStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every test of the tenant with the codes its formula reads (INV-CAT-009).</summary>
    Task<IReadOnlyList<LabTestFormulaNode>> FormulaGraphAsync(CancellationToken cancellationToken);

    Task<Paged<LabTestListItem>> ListAsync(LabTestListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<LabTestListItem>> ExportAsync(LabTestListFilter filter, CancellationToken cancellationToken);

    Task<LabTestDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Active tests for pickers, by code or names.</summary>
    Task<Paged<TestLookupItem>> LookupAsync(string? search, LabTestType? type, PageRequest page, CancellationToken cancellationToken);
}
