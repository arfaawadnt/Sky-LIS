using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Pricing;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Domain.Authorization;
using Platform.Domain.Registration;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration.Queries;

// Registers of bookings (CP-07) and batches (CP-08, CP-09).

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record ListBookingsQuery(string? Status, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? PhlebotomistUserId, string? Search, Guid? BranchId, int? Page, int? PageSize) : IQuery<Paged<BookingListItem>>;

public sealed class ListBookingsValidator : AbstractValidator<ListBookingsQuery>
{
    public ListBookingsValidator()
    {
        RuleFor(query => query.Status).IsEnumName(typeof(BookingStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Search).MaximumLength(200);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListBookingsHandler(IBookingRepository bookings, IPatientLookup patients, IPayerLookup payers, IUserDirectory users) : IQueryHandler<ListBookingsQuery, Paged<BookingListItem>>
{
    public async Task<Paged<BookingListItem>> Handle(ListBookingsQuery query, CancellationToken cancellationToken)
    {
        var page = await bookings.ListAsync(BookingFilters.Of(query.Status, query.FromUtc, query.ToUtc, query.PhlebotomistUserId, query.Search, query.BranchId), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var items = await BookingRows.ComposeAsync(page.Items, patients, payers, users, cancellationToken);
        return new Paged<BookingListItem>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Export)]
public sealed record ExportBookingsQuery(string? Status, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? PhlebotomistUserId, string? Search, Guid? BranchId) : IQuery<IReadOnlyList<BookingListItem>>;

public sealed class ExportBookingsValidator : AbstractValidator<ExportBookingsQuery>
{
    public ExportBookingsValidator() =>
        RuleFor(query => query.Status).IsEnumName(typeof(BookingStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
}

public sealed class ExportBookingsHandler(IBookingRepository bookings, IPatientLookup patients, IPayerLookup payers, IUserDirectory users) : IQueryHandler<ExportBookingsQuery, IReadOnlyList<BookingListItem>>
{
    public async Task<IReadOnlyList<BookingListItem>> Handle(ExportBookingsQuery query, CancellationToken cancellationToken)
    {
        var rows = await bookings.ListAllAsync(BookingFilters.Of(query.Status, query.FromUtc, query.ToUtc, query.PhlebotomistUserId, query.Search, query.BranchId), ExportVisitsHandler.MaxRows, cancellationToken);
        if (rows.Count > ExportVisitsHandler.MaxRows)
        {
            throw new DomainException(RegistrationCodes.ExportTooLarge, $"The export is limited to {ExportVisitsHandler.MaxRows} bookings; narrow the filters.", DomainFault.Validation);
        }

        var items = new List<BookingListItem>(rows.Count);
        foreach (var chunk in rows.Chunk(400))
        {
            items.AddRange(await BookingRows.ComposeAsync(chunk, patients, payers, users, cancellationToken));
        }

        return items;
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetBookingQuery(Guid BookingId) : IQuery<BookingDetail>;

public sealed class GetBookingHandler(IBookingRepository bookings, IPatientLookup patients, IPayerLookup payers, IUserDirectory users, ICatalogueLookup catalogue) : IQueryHandler<GetBookingQuery, BookingDetail>
{
    public async Task<BookingDetail> Handle(GetBookingQuery query, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetReadOnlyAsync(query.BookingId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.BookingNotFound, "Booking not found.", DomainFault.NotFound);
        var summary = (await BookingRows.ComposeAsync([booking], patients, payers, users, cancellationToken))[0];
        var services = (await catalogue.GetServicesAsync(booking.ServiceIds, cancellationToken)).ToDictionary(service => service.Id);
        var actors = await users.GetNamesAsync([.. new[] { booking.ConvertedByUserId, booking.CancelledByUserId }.Where(id => id.HasValue).Select(id => id!.Value)], cancellationToken);

        return new BookingDetail(
            summary,
            booking.Gender,
            booking.AgeYears,
            booking.ContractTierId,
            booking.Notes,
            [.. booking.ServiceIds.Select(id => services.TryGetValue(id, out var service)
                ? new BookingServiceItem(id, service.Code, service.NameEn, service.NameAr)
                : new BookingServiceItem(id, string.Empty, string.Empty, string.Empty))],
            booking.ConvertedAtUtc,
            booking.ConvertedByUserId is { } converter ? actors.GetValueOrDefault(converter)?.DisplayName : null,
            booking.CancellationReason,
            booking.CancelledByUserId is { } canceller ? actors.GetValueOrDefault(canceller)?.DisplayName : null,
            booking.CancelledAtUtc,
            booking.RowVersion);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record ListBatchesQuery(string? Kind, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, string? Search, Guid? BranchId, int? Page, int? PageSize) : IQuery<Paged<BatchListItem>>;

public sealed class ListBatchesValidator : AbstractValidator<ListBatchesQuery>
{
    public ListBatchesValidator()
    {
        RuleFor(query => query.Kind).IsEnumName(typeof(BatchKind), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Kind));
        RuleFor(query => query.Search).MaximumLength(200);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListBatchesHandler(IBatchRepository batches, IPayerLookup payers, ISendingLabLookup sendingLabs) : IQueryHandler<ListBatchesQuery, Paged<BatchListItem>>
{
    public async Task<Paged<BatchListItem>> Handle(ListBatchesQuery query, CancellationToken cancellationToken)
    {
        BatchKind? kind = string.IsNullOrEmpty(query.Kind) ? null : Enum.Parse<BatchKind>(query.Kind, ignoreCase: true);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var page = await batches.ListAsync(new BatchFilter(kind, query.FromUtc, query.ToUtc, search, query.BranchId), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var names = new ContractNames(payers);
        var labs = new Dictionary<Guid, SendingLabOption?>();
        var items = new List<BatchListItem>(page.Items.Count);
        foreach (var row in page.Items)
        {
            items.Add(await BatchViews.ItemAsync(row, names, labs, sendingLabs, cancellationToken));
        }

        return new Paged<BatchListItem>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetBatchQuery(Guid BatchId) : IQuery<BatchDetail>;

public sealed class GetBatchHandler(
    IBatchRepository batches,
    IVisitRepository visits,
    IPatientLookup patients,
    IPayerLookup payers,
    IDoctorLookup doctors,
    IUserDirectory users,
    ISendingLabLookup sendingLabs,
    IVisitBalanceLookup balances,
    IUserPrivileges privileges) : IQueryHandler<GetBatchQuery, BatchDetail>
{
    public async Task<BatchDetail> Handle(GetBatchQuery query, CancellationToken cancellationToken)
    {
        var batch = await batches.GetReadOnlyAsync(query.BatchId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.BatchNotFound, "Batch not found.", DomainFault.NotFound);
        var rows = await visits.ListByBatchAsync(batch.Id, cancellationToken);
        var summary = await BatchViews.ItemAsync(
            new BatchRow(batch, rows.Sum(row => row.PatientShareTotal), rows.Sum(row => row.PayerShareTotal)),
            new ContractNames(payers),
            [],
            sendingLabs,
            cancellationToken);
        var doctor = batch.ReferringDoctorId is { } doctorId ? await doctors.GetAsync(doctorId, cancellationToken) : null;
        var people = await patients.GetManyAsync([.. rows.Select(row => row.PatientId).Distinct()], cancellationToken);
        var money = await balances.GetManyAsync([.. rows.Select(row => row.Id)], cancellationToken);
        var registeredBy = (await users.GetNamesAsync([batch.RegisteredByUserId], cancellationToken)).GetValueOrDefault(batch.RegisteredByUserId)?.DisplayName;
        var canSeeVip = privileges.SpecialRights.CanSeeVip;

        var members = rows.Select(row =>
        {
            var person = people.GetValueOrDefault(row.PatientId);
            var masked = person is { IsVip: true } && !canSeeVip;
            return new BatchMemberItem(row.Id, row.Accession, row.PatientId, person?.Number, masked ? null : person?.FullName, row.Status.ToString(), row.CollectedOutside, row.PatientShareTotal, row.PayerShareTotal, money.GetValueOrDefault(row.Id)?.Remaining, row.Lines);
        }).ToList();

        return new BatchDetail(summary, batch.ReferringDoctorId, doctor?.NameEn, doctor?.NameAr, batch.CourierEn, batch.CourierAr, batch.Notes, registeredBy, members);
    }
}

internal static class BookingFilters
{
    public static BookingFilter Of(string? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? phlebotomist, string? search, Guid? branchId) =>
        new(
            string.IsNullOrEmpty(status) ? null : Enum.Parse<BookingStatus>(status, ignoreCase: true),
            fromUtc,
            toUtc,
            phlebotomist,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            branchId);
}

internal static class BookingRows
{
    public static async Task<IReadOnlyList<BookingListItem>> ComposeAsync(IReadOnlyList<Booking> rows, IPatientLookup patients, IPayerLookup payers, IUserDirectory users, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var people = await patients.GetManyAsync([.. rows.Where(row => row.PatientId.HasValue).Select(row => row.PatientId!.Value).Distinct()], cancellationToken);
        var staff = await users.GetNamesAsync([.. rows.Where(row => row.PhlebotomistUserId.HasValue).Select(row => row.PhlebotomistUserId!.Value).Distinct()], cancellationToken);
        var names = new ContractNames(payers);
        var items = new List<BookingListItem>(rows.Count);
        foreach (var row in rows)
        {
            var person = row.PatientId is { } patientId ? people.GetValueOrDefault(patientId) : null;
            var payer = await names.PayerAsync(row.PayerId, cancellationToken);
            var tier = await names.TierAsync(row.ContractTierId, cancellationToken);
            items.Add(new BookingListItem(
                row.Id,
                row.Number,
                row.Status.ToString(),
                row.VisitAtUtc,
                row.BranchId,
                row.PatientId,
                person?.Number,
                person?.FullName ?? row.PatientName,
                person?.Mobile ?? row.Mobile,
                row.Address,
                row.PhlebotomistUserId,
                row.PhlebotomistUserId is { } phlebotomist ? staff.GetValueOrDefault(phlebotomist)?.DisplayName : null,
                row.PayerId,
                payer?.NameEn,
                payer?.NameAr,
                tier?.NameEn,
                tier?.NameAr,
                row.ServiceIds.Length,
                row.VisitId,
                row.Accession));
        }

        return items;
    }
}

internal static class BatchViews
{
    public static async Task<BatchListItem> ItemAsync(BatchRow row, ContractNames names, Dictionary<Guid, SendingLabOption?> labs, ISendingLabLookup sendingLabs, CancellationToken cancellationToken)
    {
        var batch = row.Batch;
        SendingLabOption? lab = null;
        if (batch.SendingLabId is { } labId)
        {
            if (!labs.TryGetValue(labId, out lab))
            {
                lab = await sendingLabs.GetAsync(labId, cancellationToken);
                labs[labId] = lab;
            }
        }

        var payer = await names.PayerAsync(batch.PayerId, cancellationToken);
        var tier = await names.TierAsync(batch.ContractTierId, cancellationToken);
        return new BatchListItem(
            batch.Id,
            batch.Number,
            batch.Kind.ToString(),
            batch.RegisteredAtUtc,
            batch.BranchId,
            batch.GroupName,
            batch.SendingLabId,
            lab?.NameEn,
            lab?.NameAr,
            batch.PayerId,
            payer?.NameEn,
            payer?.NameAr,
            tier?.NameEn,
            tier?.NameAr,
            batch.MemberCount,
            row.PatientShareTotal,
            row.PayerShareTotal);
    }
}
