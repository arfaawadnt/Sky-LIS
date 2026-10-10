using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Domain.Registration;

namespace Platform.Application.Registration;

/// <summary>Filters of the visit register (CP-10); <paramref name="IncludeVip"/> false keeps VIP patients out of name and mobile matches (intake Q12).</summary>
public sealed record VisitSearchFilter(
    string? Accession,
    string? PatientNumber,
    string? Name,
    string? Mobile,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? BranchId,
    Guid? PayerId,
    VisitStatus? Status,
    Guid? PatientId,
    Guid? BatchId,
    bool IncludeVip);

/// <summary>A visit row of the register before names and balances are joined by the handler.</summary>
public sealed record VisitRow(
    Guid Id,
    string Accession,
    DateTimeOffset RegisteredAtUtc,
    Guid BranchId,
    Guid PatientId,
    Guid PayerId,
    Guid ContractTierId,
    VisitStatus Status,
    VisitLocationType LocationType,
    bool Urgent,
    Guid? BatchId,
    bool CollectedOutside,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    IReadOnlyList<VisitLineChip> Lines);

/// <summary>A previous order of a test for the frequency check (BR-12).</summary>
public sealed record PriorTestOrder(Guid TestId, Guid VisitId, string Accession, DateTimeOffset OrderedAtUtc);

public sealed record CancelledLineRow(
    Guid LineId,
    Guid VisitId,
    string Accession,
    Guid PatientId,
    Guid BranchId,
    string Code,
    string NameEn,
    string NameAr,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    string? Reason,
    Guid? CancelledByUserId,
    DateTimeOffset? CancelledAtUtc);

public sealed record TransferRow(
    Guid Id,
    Guid VisitId,
    string Accession,
    VisitTransferKind Kind,
    Guid? LineId,
    string? ServiceCode,
    Guid? FromPayerId,
    Guid? FromContractTierId,
    Guid? ToPayerId,
    Guid? ToContractTierId,
    decimal PatientShareBefore,
    decimal PatientShareAfter,
    decimal PayerShareBefore,
    decimal PayerShareAfter,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset AtUtc);

/// <summary>Write side of visits plus the projections of the module's registers. Every read is bounded to the current tenant.</summary>
public interface IVisitRepository : IRepository<Visit>
{
    /// <summary>The visit with its lines, tubes, answers and transfers, untracked (documents and detail).</summary>
    Task<Visit?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken);

    Task<Guid?> FindIdByAccessionAsync(string accession, CancellationToken cancellationToken);

    /// <summary>Visits of a file, tracked, for the merge policy.</summary>
    Task<IReadOnlyList<Visit>> ListByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>BR-12: non-cancelled lines of the patient's other visits ordered since <paramref name="sinceUtc"/> that carry one of the tests.</summary>
    Task<IReadOnlyList<PriorTestOrder>> PriorTestOrdersAsync(Guid patientId, IReadOnlyCollection<Guid> testIds, DateTimeOffset sinceUtc, Guid? excludeVisitId, CancellationToken cancellationToken);

    Task<Paged<VisitRow>> SearchAsync(VisitSearchFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Every match up to <paramref name="limit"/> + 1 rows (exports refuse more than the limit).</summary>
    Task<IReadOnlyList<VisitRow>> SearchAllAsync(VisitSearchFilter filter, int limit, CancellationToken cancellationToken);

    /// <summary>The position of the visit among the patient's visits (job order "this is the visit No. N").</summary>
    Task<int> VisitOrdinalAsync(Guid patientId, DateTimeOffset registeredAtUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<VisitRow>> ListByBatchAsync(Guid batchId, CancellationToken cancellationToken);

    Task<Paged<CancelledLineRow>> CancelledLinesAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? branchId, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<CancelledLineRow>> CancelledLinesAllAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? branchId, int limit, CancellationToken cancellationToken);

    Task<Paged<TransferRow>> TransfersAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, VisitTransferKind? kind, Guid? visitId, PageRequest page, CancellationToken cancellationToken);
}

public sealed record BookingFilter(BookingStatus? Status, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? PhlebotomistUserId, string? Search, Guid? BranchId);

public interface IBookingRepository : IRepository<Booking>
{
    Task<Booking?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken);

    Task<Paged<Booking>> ListAsync(BookingFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ListAllAsync(BookingFilter filter, int limit, CancellationToken cancellationToken);

    /// <summary>Booked bookings of a file, tracked, for the merge policy.</summary>
    Task<IReadOnlyList<Booking>> ListBookedByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken);
}

public sealed record BatchFilter(BatchKind? Kind, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, string? Search, Guid? BranchId);

/// <summary>A batch with the sums of its visits.</summary>
public sealed record BatchRow(Batch Batch, decimal PatientShareTotal, decimal PayerShareTotal);

public interface IBatchRepository : IRepository<Batch>
{
    Task<Batch?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken);

    Task<Paged<BatchRow>> ListAsync(BatchFilter filter, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>
/// Service entry at the registration desk (CP-06; LDM §4.4 "shortcut number + Enter"): exact matches on shortcut number,
/// code, short name or alias among active orderable services, and candidates for the picker. Raw SQL on Catalogue A's
/// <c>app.services</c> (same-module contract columns); adapter in Persistence/Registration.
/// </summary>
public interface IServiceEntryFinder
{
    Task<IReadOnlyList<ServiceEntryItem>> FindExactAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceEntryItem>> SearchAsync(string text, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceEntryItem>> GetManyAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);
}

/// <summary>Sending laboratories (Catalogue B, CP-50) for lab-to-lab registration (CP-09): payer, tier, courier, phone, doctor.</summary>
public interface ISendingLabLookup
{
    Task<SendingLabOption?> GetAsync(Guid sendingLabId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SendingLabOption>> ListActiveAsync(CancellationToken cancellationToken);
}

/// <summary>The letterhead of the module's documents: the laboratory (tenant) and the branch names; blanks when a source is missing.</summary>
public interface IRegistrationDirectory
{
    Task<RegistrationLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken);
}
