using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>Write side and register of <see cref="SampleCompatibilityGroup"/> (LIS-CR-015 M4); the aggregate loads with its members.</summary>
public interface ISampleCompatibilityGroupRepository : IRepository<SampleCompatibilityGroup>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>The sample types (with their conditions) members name, read only, so the aggregate can check them.</summary>
    Task<IReadOnlyList<SampleType>> SampleTypesAsync(IReadOnlyCollection<Guid> sampleTypeIds, CancellationToken cancellationToken);

    /// <summary>The first of <paramref name="pairs"/> that is already a member of another active group (CAT-284), or null.</summary>
    Task<CompatibilityConflict?> FindActiveHolderAsync(IReadOnlyCollection<CompatibleSamplePair> pairs, Guid excludeGroupId, CancellationToken cancellationToken);

    Task<Paged<SampleCompatibilityGroupView>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<SampleCompatibilityGroupView>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken);

    Task<SampleCompatibilityGroupView?> GetViewAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>A pair already held by another active group: that group's code and the pair's codes, for the refusal message.</summary>
public sealed record CompatibilityConflict(string GroupCode, string SampleTypeCode, string? SampleConditionCode);
