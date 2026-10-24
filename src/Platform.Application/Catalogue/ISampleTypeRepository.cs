using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>Write side and registers of <see cref="SampleType"/> (the aggregate loads with its conditions).</summary>
public interface ISampleTypeRepository : IRepository<SampleType>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<SampleTypeListItem>> ListAsync(SampleTypeListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<SampleTypeListItem>> ExportAsync(SampleTypeListFilter filter, CancellationToken cancellationToken);

    Task<SampleTypeDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}
