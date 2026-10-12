using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Treasury;
using Platform.Domain.Treasury;

namespace Platform.Application.Treasury;

/// <summary>Write side plus the projections the module's registers need (ADR-0010 carried forward). Every read is bounded to the current tenant.</summary>
public interface ITreasuryRepository : IRepository<Domain.Treasury.Treasury>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="search"/> (trimmed, or null) matches the code and both names, whatever the UI language (UAT B-08);
    /// <paramref name="sort"/> orders the whole list before paging (validation v0.5.0 B-03, <see cref="TreasurySortKeys.Treasuries"/>).
    /// </summary>
    Task<Paged<TreasuryListItem>> ListAsync(string? search, bool? isActive, ListSort sort, PageRequest page, CancellationToken cancellationToken);

    Task<TreasuryListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record TreasuryAccessRequestFilter(TreasuryAccessStatus? Status, Guid? TreasuryId, Guid? RequestedByUserId);

public interface ITreasuryAccessRequestRepository : IRepository<TreasuryAccessRequest>
{
    /// <summary>INV-TRS-002: a Pending or Approved request already exists for this user, treasury and device.</summary>
    Task<bool> LiveRequestExistsAsync(Guid requestedByUserId, Guid treasuryId, string deviceId, CancellationToken cancellationToken);

    Task<Paged<TreasuryAccessRequestListItem>> ListAsync(TreasuryAccessRequestFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<TreasuryAccessRequestDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}
