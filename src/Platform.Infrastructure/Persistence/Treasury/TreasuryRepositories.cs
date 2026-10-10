using Microsoft.EntityFrameworkCore;
using Platform.Application.Treasury;
using Platform.Contracts.Common;
using Platform.Contracts.Treasury;
using Platform.Domain.Treasury;

namespace Platform.Infrastructure.Persistence.Treasury;

internal sealed class TreasuryRepository(AppDbContext context) : Repository<Domain.Treasury.Treasury>(context), ITreasuryRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(treasury => treasury.Code == cleaned, cancellationToken);
    }

    public async Task<Paged<TreasuryListItem>> ListAsync(bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (isActive is { } active)
        {
            query = query.Where(treasury => treasury.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(treasury => treasury.Code)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(treasury => new TreasuryListItem(treasury.Id, treasury.Code, treasury.Name.En, treasury.Name.Ar, treasury.BranchId, treasury.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<TreasuryListItem>(items, page.Page, page.PageSize, total);
    }

    public Task<TreasuryListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking()
            .Where(treasury => treasury.Id == id)
            .Select(treasury => new TreasuryListItem(treasury.Id, treasury.Code, treasury.Name.En, treasury.Name.Ar, treasury.BranchId, treasury.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
}

internal sealed class TreasuryAccessRequestRepository(AppDbContext context) : Repository<TreasuryAccessRequest>(context), ITreasuryAccessRequestRepository
{
    public Task<bool> LiveRequestExistsAsync(Guid requestedByUserId, Guid treasuryId, string deviceId, CancellationToken cancellationToken) =>
        Set.AnyAsync(
            request => request.RequestedByUserId == requestedByUserId
                       && request.TreasuryId == treasuryId
                       && request.DeviceId == deviceId
                       && (request.Status == TreasuryAccessStatus.Pending || request.Status == TreasuryAccessStatus.Approved),
            cancellationToken);

    public async Task<Paged<TreasuryAccessRequestListItem>> ListAsync(TreasuryAccessRequestFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(request => request.Status == status);
        }

        if (filter.TreasuryId is { } treasuryId)
        {
            query = query.Where(request => request.TreasuryId == treasuryId);
        }

        if (filter.RequestedByUserId is { } requestedBy)
        {
            query = query.Where(request => request.RequestedByUserId == requestedBy);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from request in query.OrderByDescending(request => request.RequestedAtUtc).Skip(page.Skip).Take(page.PageSize)
            join treasury in Context.Treasuries.AsNoTracking() on request.TreasuryId equals treasury.Id
            join requester in Context.PlatformUsers.AsNoTracking() on request.RequestedByUserId equals requester.Id
            from decider in Context.PlatformUsers.AsNoTracking().Where(user => user.Id == request.DecidedByUserId).DefaultIfEmpty()
            select new
            {
                request.Id,
                request.Number,
                request.TreasuryId,
                treasury.Code,
                NameEn = treasury.Name.En,
                NameAr = treasury.Name.Ar,
                request.BranchId,
                request.RequestedByUserId,
                RequesterName = requester.DisplayName,
                request.DeviceId,
                request.DeviceLabel,
                request.Status,
                request.RequestedAtUtc,
                request.DecidedAtUtc,
                DeciderName = decider == null ? null : decider.DisplayName,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.Select(row => new TreasuryAccessRequestListItem(
            row.Id, row.Number, row.TreasuryId, row.Code, row.NameEn, row.NameAr, row.BranchId, row.RequestedByUserId, row.RequesterName,
            row.DeviceId, row.DeviceLabel, row.Status.ToString(), row.RequestedAtUtc, row.DecidedAtUtc, row.DeciderName)).ToList();

        return new Paged<TreasuryAccessRequestListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<TreasuryAccessRequestDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await (
            from request in Set.AsNoTracking().Where(request => request.Id == id)
            join treasury in Context.Treasuries.AsNoTracking() on request.TreasuryId equals treasury.Id
            join requester in Context.PlatformUsers.AsNoTracking() on request.RequestedByUserId equals requester.Id
            from decider in Context.PlatformUsers.AsNoTracking().Where(user => user.Id == request.DecidedByUserId).DefaultIfEmpty()
            from approver in Context.PlatformUsers.AsNoTracking().Where(user => request.Approval != null && user.Id == request.Approval.ActorUserId).DefaultIfEmpty()
            select new
            {
                request.Id,
                request.Number,
                request.TreasuryId,
                treasury.Code,
                NameEn = treasury.Name.En,
                NameAr = treasury.Name.Ar,
                request.BranchId,
                request.RequestedByUserId,
                RequesterName = requester.DisplayName,
                request.DeviceId,
                request.DeviceLabel,
                request.Status,
                request.RequestedAtUtc,
                request.Approval,
                ApproverName = approver == null ? null : approver.DisplayName,
                request.DecidedByUserId,
                DeciderName = decider == null ? null : decider.DisplayName,
                request.DecidedAtUtc,
                request.DecisionReason,
            }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var approval = row.Approval is null ? null : new ApprovalView(row.Approval.ActorUserId, row.ApproverName ?? string.Empty, row.Approval.AtUtc, row.Approval.Comment);
        return new TreasuryAccessRequestDetail(
            row.Id, row.Number, row.TreasuryId, row.Code, row.NameEn, row.NameAr, row.BranchId, row.RequestedByUserId, row.RequesterName,
            row.DeviceId, row.DeviceLabel, row.Status.ToString(), row.RequestedAtUtc, approval, row.DecidedByUserId, row.DeciderName, row.DecidedAtUtc, row.DecisionReason);
    }
}
