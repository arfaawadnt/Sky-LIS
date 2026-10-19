using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>Write side and registers of <see cref="Service"/> (the aggregate loads with its members).</summary>
public interface IServiceRepository : IRepository<Service>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>INV-CAT-005: another service already uses the shortcut number.</summary>
    Task<bool> ShortcutTakenAsync(int shortcutNo, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>The next free sequence of the tenant (max + 1).</summary>
    Task<int> NextSequenceAsync(CancellationToken cancellationToken);

    /// <summary>Every service's sequence in the tenant (INV-CAT-004 is checked over all of them).</summary>
    Task<IReadOnlyDictionary<Guid, int>> SequencesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Service>> GetManyForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every service of the tenant that carries the test as a member, tracked with its members, in the global order (T21, parent services).</summary>
    Task<IReadOnlyList<Service>> GetCarryingTestForUpdateAsync(Guid testId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ServiceMemberState>> MemberStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<Paged<ServiceListItem>> ListAsync(ServiceListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceListItem>> ExportAsync(ServiceListFilter filter, CancellationToken cancellationToken);

    Task<ServiceDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceSequenceRow>> SequenceRowsAsync(string? search, ServiceType? type, Guid? clinicalGroupId, CancellationToken cancellationToken);

    /// <summary>Active services for pickers, by code, short name, names, aliases or shortcut number.</summary>
    Task<Paged<ServiceLookupItem>> LookupAsync(string? search, ServiceType? type, bool? orderable, PageRequest page, CancellationToken cancellationToken);
}
