using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

/// <summary>The patient of a booking: an existing file, or the data to find or create it at conversion (intake Q14).</summary>
public sealed record BookingPatient(Guid? PatientId, string? Name, string? Gender, string? Mobile, int? AgeYears);

/// <summary>The editable content of a booking (B1, B2).</summary>
public sealed record BookingContent(
    BookingPatient Patient,
    string Address,
    DateTimeOffset VisitAtUtc,
    Guid? PhlebotomistUserId,
    Guid PayerId,
    Guid ContractTierId,
    IReadOnlyList<Guid> ServiceIds,
    string? Notes);

/// <summary>
/// A home reservation (SRS CP-07, LDM §4.8, WF-03): a home draw booked with a mandatory address, a visit time, an
/// assigned phlebotomist, a contract and the services. It is not a registration: converting it registers the visit
/// (with the booking id, collector = phlebotomist) in the same unit of work and freezes the booking.
/// </summary>
public sealed class Booking : AggregateRoot, ITenantScoped
{
    public const int MaxNumberLength = 20;
    public const int MaxNameLength = 200;
    public const int MaxMobileLength = 20;
    public const int MaxAddressLength = 500;
    public const int MaxNotesLength = 1000;
    public const int MaxServices = 100;
    public const int MaxReasonLength = 1000;
    public const string NumberKind = "booking";
    public const string NumberPrefix = "BKG";

    private Booking()
    {
        Number = string.Empty;
        Address = string.Empty;
        ServiceIds = [];
    }

    private Booking(string number, Guid branchId)
        : base(Guid.CreateVersion7())
    {
        Number = number;
        BranchId = branchId;
        Address = string.Empty;
        ServiceIds = [];
        Status = BookingStatus.Booked;
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid? PatientId { get; private set; }

    public string? PatientName { get; private set; }

    public string? Gender { get; private set; }

    public string? Mobile { get; private set; }

    public int? AgeYears { get; private set; }

    public string Address { get; private set; }

    public DateTimeOffset VisitAtUtc { get; private set; }

    public Guid? PhlebotomistUserId { get; private set; }

    public Guid PayerId { get; private set; }

    public Guid ContractTierId { get; private set; }

    public Guid[] ServiceIds { get; private set; }

    public string? Notes { get; private set; }

    public BookingStatus Status { get; private set; }

    public Guid? VisitId { get; private set; }

    public string? Accession { get; private set; }

    public DateTimeOffset? ConvertedAtUtc { get; private set; }

    public Guid? ConvertedByUserId { get; private set; }

    public string? CancellationReason { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>B1 Create (<c>registration.create</c>).</summary>
    public static Booking Create(string number, Guid branchId, BookingContent content, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(content);
        var cleanedNumber = (number ?? string.Empty).Trim();
        Require(cleanedNumber.Length is >= 1 and <= MaxNumberLength, RegistrationCodes.HeaderInvalid, "A booking needs a number.", DomainFault.Validation);
        Require(branchId != Guid.Empty, RegistrationCodes.HeaderInvalid, "A booking needs the session branch.", DomainFault.Validation);

        var booking = new Booking(cleanedNumber, branchId);
        booking.Apply(content, nowUtc);
        booking.Raise(new BookingCreated(booking.Id, booking.Number, branchId, booking.VisitAtUtc, nowUtc));
        return booking;
    }

    /// <summary>B2 Edit (<c>registration.edit</c>): Booked only.</summary>
    public void Update(BookingContent content, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(content);
        RequireBooked("edit");
        Apply(content, nowUtc);
    }

    /// <summary>B3 Cancel (<c>registration.cancel</c>): Booked only; reason mandatory.</summary>
    public void Cancel(Guid actorUserId, string reason, DateTimeOffset nowUtc)
    {
        RequireBooked("cancel");
        var cleaned = (reason ?? string.Empty).Trim();
        Require(cleaned.Length is >= 1 and <= MaxReasonLength, RegistrationCodes.CancellationReasonRequired, "A cancellation needs a reason.", DomainFault.Validation);

        Status = BookingStatus.Cancelled;
        CancellationReason = cleaned;
        CancelledByUserId = actorUserId;
        CancelledAtUtc = nowUtc;
        Raise(new BookingCancelled(Id, Number, actorUserId, cleaned, nowUtc));
    }

    /// <summary>B4 Convert (part of V1 with the booking): Booked only; a booking that names a file converts only on that file (REG-108).</summary>
    public void Convert(Guid visitId, string accession, Guid patientId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        RequireBooked("convert");
        EnsureConvertibleFor(patientId);
        VisitId = visitId;
        Accession = accession;
        PatientId ??= patientId;
        Status = BookingStatus.Converted;
        ConvertedAtUtc = nowUtc;
        ConvertedByUserId = actorUserId;
        Raise(new BookingConverted(Id, Number, visitId, accession, actorUserId, nowUtc));
    }

    /// <summary>The checks of B4 before the visit is built (same codes as <see cref="Convert"/>).</summary>
    public void EnsureConvertibleFor(Guid patientId)
    {
        RequireBooked("convert");
        if (PatientId is { } booked && booked != patientId)
        {
            throw new DomainException(RegistrationCodes.BookingPatientMismatch, "The booking is for another patient file.", DomainFault.Conflict);
        }
    }

    /// <summary>B5 Re-point the booking's file after a merge (policy); Booked only, idempotent.</summary>
    public bool RepointPatient(Guid fromPatientId, Guid toPatientId, string reason, DateTimeOffset nowUtc)
    {
        if (Status != BookingStatus.Booked || PatientId != fromPatientId || toPatientId == Guid.Empty || toPatientId == fromPatientId)
        {
            return false;
        }

        PatientId = toPatientId;
        Raise(new BookingPatientRepointed(Id, Number, fromPatientId, toPatientId, string.IsNullOrWhiteSpace(reason) ? "Patient files merged" : reason.Trim(), nowUtc));
        return true;
    }

    private void Apply(BookingContent content, DateTimeOffset nowUtc)
    {
        var patient = content.Patient ?? new BookingPatient(null, null, null, null, null);
        var name = Blank(patient.Name);
        var mobile = Blank(patient.Mobile);
        Require(
            patient.PatientId is { } id ? id != Guid.Empty : name is not null && mobile is not null,
            RegistrationCodes.BookingPatientRequired,
            "Choose the patient file, or give the patient's name and mobile.",
            DomainFault.Validation);
        Require((name?.Length ?? 0) <= MaxNameLength && (mobile?.Length ?? 0) <= MaxMobileLength, RegistrationCodes.BookingPatientRequired, "The patient's name or mobile is too long.", DomainFault.Validation);
        Require(patient.Gender is null or "Male" or "Female", RegistrationCodes.BookingPatientRequired, "The gender is Male or Female.", DomainFault.Validation);
        Require(patient.AgeYears is null or >= 0 and <= 150, RegistrationCodes.BookingPatientRequired, "The age is between 0 and 150 years.", DomainFault.Validation);

        var address = (content.Address ?? string.Empty).Trim();
        Require(address.Length is >= 1 and <= MaxAddressLength, RegistrationCodes.BookingAddressRequired, "A home visit needs the address.", DomainFault.Validation);
        Require(content.VisitAtUtc >= nowUtc.AddDays(-1), RegistrationCodes.BookingTimeInvalid, "The visit time cannot be in the past.", DomainFault.Validation);
        Require(content.PayerId != Guid.Empty, RegistrationCodes.PayerInvalid, "A booking needs a payer.", DomainFault.Validation);
        Require(content.ContractTierId != Guid.Empty, RegistrationCodes.TierInvalid, "A booking needs a contract tier.", DomainFault.Validation);

        var services = (content.ServiceIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        Require(services.Length > 0, RegistrationCodes.LinesRequired, "Add at least one service.", DomainFault.Validation);
        Require(services.Length <= MaxServices, RegistrationCodes.TooManyLines, $"A booking holds at most {MaxServices} services.", DomainFault.Validation);

        var notes = Blank(content.Notes);
        Require((notes?.Length ?? 0) <= MaxNotesLength, RegistrationCodes.HeaderInvalid, $"Notes have at most {MaxNotesLength} characters.", DomainFault.Validation);

        PatientId = patient.PatientId;
        PatientName = name;
        Gender = patient.Gender;
        Mobile = mobile;
        AgeYears = patient.AgeYears;
        Address = address;
        VisitAtUtc = content.VisitAtUtc;
        PhlebotomistUserId = content.PhlebotomistUserId;
        PayerId = content.PayerId;
        ContractTierId = content.ContractTierId;
        ServiceIds = services;
        Notes = notes;
    }

    private void RequireBooked(string action)
    {
        if (Status != BookingStatus.Booked)
        {
            throw new InvalidStateTransitionException(RegistrationCodes.BookingNotBooked, "booking", Status.ToString(), action);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
