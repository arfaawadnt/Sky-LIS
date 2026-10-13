using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
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

    private static readonly SortColumns<Domain.Treasury.Treasury> Sorts = new SortColumns<Domain.Treasury.Treasury>(TreasurySortKeys.Treasuries)
        .Column("code", treasury => treasury.Code)
        .Column("nameEn", treasury => treasury.Name.En)
        .Column("nameAr", treasury => treasury.Name.Ar)
        .ThenBy(treasury => treasury.Code)
        .ThenBy(treasury => treasury.Id);

    public async Task<Paged<TreasuryListItem>> ListAsync(string? search, bool? isActive, ListSort sort, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // UAT B-08: code and both names, whatever the UI language (an Arabic name is found from the English UI)
            var pattern = ContainsPattern(search);
            query = query.Where(treasury => EF.Functions.ILike(treasury.Code, pattern, LikeEscape) || EF.Functions.ILike(treasury.Name.En, pattern, LikeEscape) || EF.Functions.ILike(treasury.Name.Ar, pattern, LikeEscape));
        }

        if (isActive is { } active)
        {
            query = query.Where(treasury => treasury.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await Sorts.Apply(query, sort, treasuries => treasuries.OrderBy(treasury => treasury.Code))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(treasury => new TreasuryListItem(treasury.Id, treasury.Code, treasury.Name.En, treasury.Name.Ar, treasury.BranchId, treasury.IsActive, null, null, null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<TreasuryListItem>(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// The escape character of <see cref="ContainsPattern"/>. Passed explicitly: without it Npgsql translates
    /// <c>ILike</c> with <c>ESCAPE ''</c>, and an escaped <c>_</c> would match nothing.
    /// </summary>
    private const string LikeEscape = "\\";

    /// <summary><c>%text%</c> with the LIKE wildcards of the text escaped (a typed <c>_</c> or <c>%</c> is literal).</summary>
    private static string ContainsPattern(string search)
    {
        var escaped = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return "%" + escaped + "%";
    }

    public Task<TreasuryListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking()
            .Where(treasury => treasury.Id == id)
            .Select(treasury => new TreasuryListItem(treasury.Id, treasury.Code, treasury.Name.En, treasury.Name.Ar, treasury.BranchId, treasury.IsActive, null, null, null))
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
                request.DecisionReason,
                ApprovalComment = request.Approval == null ? null : request.Approval.Comment,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.Select(row => new TreasuryAccessRequestListItem(
            row.Id, row.Number, row.TreasuryId, row.Code, row.NameEn, row.NameAr, row.BranchId, row.RequestedByUserId, row.RequesterName,
            row.DeviceId, row.DeviceLabel, row.Status.ToString(), row.RequestedAtUtc, row.DecidedAtUtc, row.DeciderName, row.DecisionReason, row.ApprovalComment)).ToList();

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
