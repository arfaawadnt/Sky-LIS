using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Patients;

/// <summary>The editable demographics of a patient file as entered (CP-06 / CP-11); <see cref="Patient"/> validates and cleans them.</summary>
public sealed record PatientDemographics(
    PatientTitle Title,
    string FullName,
    string? NameAr,
    PatientGender Gender,
    DateOnly? DateOfBirth,
    PatientAge? Age,
    Guid? PatientConditionId,
    string? Mobile,
    string? NationalId,
    string? Nationality,
    string? Email,
    Guid? GovernorateId,
    string? City,
    string? Address,
    string? Comments);

/// <summary>The tenant parameters that make a field mandatory (CP-57: <c>mandatoryMobile</c>, <c>mandatoryNationalId</c>); the handler reads them, the aggregate applies them.</summary>
public sealed record PatientFieldRules(bool MobileRequired, bool NationalIdRequired)
{
    public static readonly PatientFieldRules Optional = new(false, false);
}

/// <summary>
/// The patient file (SRS CP-11, CP-06 patient data; intake LIS-INT-PAT-001). Its number is assigned once and never
/// changes (BR-01). Either a date of birth or an age stated on a day is recorded, and the age shown is always
/// computed (<see cref="AgeOn"/>). Transitions: Register (T1), Update (T2), MergeInto (T3); Merged is frozen and
/// terminal. The tenant is stamped by the persistence layer.
/// </summary>
public sealed class Patient : AggregateRoot, ITenantScoped
{
    public const string NumberKind = "patient";
    public const string NumberPrefix = "PAT";
    public const int MaxNumberLength = 20;
    public const int MaxNameLength = 200;
    public const int MaxMobileLength = 16;
    public const int MinMobileDigits = 8;
    public const int MaxMobileDigits = 15;
    public const int MinNationalIdLength = 5;
    public const int MaxNationalIdLength = 32;
    public const int MaxNationalityLength = 64;
    public const int MaxEmailLength = 254;
    public const int MaxCityLength = 100;
    public const int MaxAddressLength = 500;
    public const int MaxCommentsLength = 2000;
    public const int MaxReasonLength = 1000;

    public static readonly DateOnly EarliestDateOfBirth = new(1900, 1, 1);

    private Patient()
    {
        Number = string.Empty;
        FullName = string.Empty;
        NameKey = string.Empty;
        NameArKey = string.Empty;
    }

    private Patient(Guid id, string number, DateOnly registeredOn)
        : base(id)
    {
        Number = number;
        RegisteredOn = registeredOn;
        Status = PatientStatus.Active;
        FullName = string.Empty;
        NameKey = string.Empty;
        NameArKey = string.Empty;
    }

    public Guid TenantId { get; private set; }

    /// <summary><c>PAT-yyyy-nnnnn</c> from the per-tenant counter (pitfall P-12); unique per tenant; never changes (INV-PAT-001).</summary>
    public string Number { get; private set; }

    public PatientTitle Title { get; private set; }

    /// <summary>The name in Latin script (required).</summary>
    public string FullName { get; private set; }

    public string? NameAr { get; private set; }

    /// <summary>Search key of <see cref="FullName"/> (<see cref="PatientNameNormalizer"/>).</summary>
    public string NameKey { get; private set; }

    /// <summary>Search key of <see cref="NameAr"/>; empty when there is no Arabic name.</summary>
    public string NameArKey { get; private set; }

    public PatientGender Gender { get; private set; }

    /// <summary>The date of birth when known; otherwise <see langword="null"/> and the stated age is kept.</summary>
    public DateOnly? DateOfBirth { get; private set; }

    public int? AgeYears { get; private set; }

    public int? AgeMonths { get; private set; }

    public int? AgeDays { get; private set; }

    /// <summary>The day the age was stated (registration or the last update that stated it); <see langword="null"/> when a date of birth is known.</summary>
    public DateOnly? AgeRecordedOn { get; private set; }

    /// <summary>The day the file was registered (server clock, UTC date).</summary>
    public DateOnly RegisteredOn { get; private set; }

    /// <summary>Catalogue patient condition (BR-08), referenced by id.</summary>
    public Guid? PatientConditionId { get; private set; }

    /// <summary>Normalised: optional <c>+</c> and 8–15 digits.</summary>
    public string? Mobile { get; private set; }

    public string? NationalId { get; private set; }

    public string? Nationality { get; private set; }

    public string? Email { get; private set; }

    public Guid? GovernorateId { get; private set; }

    public string? City { get; private set; }

    public string? Address { get; private set; }

    public string? Comments { get; private set; }

    /// <summary>Confidential: shown and changed only with <c>patients.viewConfidential</c> (INV-PAT-007).</summary>
    public bool IsVip { get; private set; }

    public PatientStatus Status { get; private set; }

    public Guid? MergedIntoPatientId { get; private set; }

    public DateTimeOffset? MergedAtUtc { get; private set; }

    public Guid? MergedByUserId { get; private set; }

    public string? MergeReason { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>The stated age, when no date of birth is known.</summary>
    public PatientAge? StatedAge => AgeYears is { } years ? new PatientAge(years, AgeMonths ?? 0, AgeDays ?? 0) : null;

    /// <summary>The date of birth, or the one implied by the stated age on the day it was stated.</summary>
    public DateOnly? EstimatedDateOfBirth => DateOfBirth ?? (StatedAge is { } age && AgeRecordedOn is { } on ? age.BirthDateOn(on) : null);

    public bool DateOfBirthIsEstimated => DateOfBirth is null;

    /// <summary>
    /// The laboratory's calendar day at <paramref name="nowUtc"/>: the date in <paramref name="zone"/> (the time zone of the
    /// laboratory's country), the UTC date when none is known. The registration day and the day an age was stated are
    /// local days (validation v0.5.0 R-11: a file created at 01:57 in Cairo on 12 Oct got the age date 11 Oct).
    /// </summary>
    public static DateOnly LocalDate(DateTimeOffset nowUtc, TimeZoneInfo? zone) =>
        DateOnly.FromDateTime(zone is null ? nowUtc.UtcDateTime : TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);

    /// <summary>Runs the field rules of T1/T2 without creating anything, so a handler refuses before it draws a number.</summary>
    public static void Check(PatientDemographics demographics, PatientFieldRules rules, DateTimeOffset nowUtc, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(demographics);
        ArgumentNullException.ThrowIfNull(rules);
        _ = Clean(demographics, rules, LocalDate(nowUtc, zone));
    }

    /// <summary>
    /// T1 Register (<c>patients.create</c>). The handler draws the number, reads the field rules from the tenant settings and
    /// decides who may set the VIP flag. <paramref name="zone"/> is the laboratory's time zone (see <see cref="LocalDate"/>).
    /// </summary>
    public static Patient Register(string number, PatientDemographics demographics, PatientFieldRules rules, bool isVip, DateTimeOffset nowUtc, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(demographics);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        var cleanedNumber = number.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cleanedNumber.Length, MaxNumberLength, nameof(number));

        var today = LocalDate(nowUtc, zone);
        var patient = new Patient(Guid.CreateVersion7(), cleanedNumber, today);
        patient.Apply(Clean(demographics, rules, today));
        patient.IsVip = isVip;
        patient.Raise(new PatientRegistered(patient.Id, patient.Number, patient.Gender, nowUtc));
        return patient;
    }

    /// <summary>
    /// T2 Update (<c>patients.edit</c>): Active only (PAT-020); the same field rules as T1; <paramref name="isVip"/>
    /// <see langword="null"/> keeps the flag. Raises <see cref="PatientUpdated"/> only when something changed. A stated
    /// age equal to the age computed today keeps the original statement (no artificial change). "Today" is the laboratory's
    /// day in <paramref name="zone"/> (see <see cref="LocalDate"/>).
    /// </summary>
    public void Update(PatientDemographics demographics, PatientFieldRules rules, bool? isVip, DateTimeOffset nowUtc, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(demographics);
        ArgumentNullException.ThrowIfNull(rules);
        RequireActive("update");

        var today = LocalDate(nowUtc, zone);
        var before = Current();
        var cleaned = Clean(demographics, rules, today);
        if (cleaned.DateOfBirth is null && DateOfBirth is null && cleaned.StatedAge is { } stated && stated == AgeOn(today))
        {
            cleaned = cleaned with { StatedAge = before.StatedAge, AgeRecordedOn = before.AgeRecordedOn };
        }

        Apply(cleaned);
        var vipChanged = isVip is { } vip && vip != IsVip;
        if (isVip is { } newVip)
        {
            IsVip = newVip;
        }

        if (vipChanged || before != Current())
        {
            Raise(new PatientUpdated(Id, Number, nowUtc));
        }
    }

    /// <summary>
    /// T3 Merge (<c>patients.manage</c>): this file (the duplicate) becomes Merged and points to
    /// <paramref name="survivor"/>, which keeps its number. Refused into itself (PAT-021), when this file is not Active
    /// (PAT-020), when the survivor is not Active (PAT-022), when the genders differ unless confirmed (PAT-100), or
    /// without a reason (PAT-024). Terminal.
    /// </summary>
    public void MergeInto(Patient survivor, Guid actorUserId, string reason, bool confirmGenderMismatch, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        Require(survivor.Id != Id, PatientCodes.MergeWithItself, "A file cannot be merged into itself.", DomainFault.Validation);
        RequireActive("merge");
        Require(survivor.Status == PatientStatus.Active, PatientCodes.SurvivorNotActive, "The surviving file must be active.", DomainFault.Conflict);
        Require(survivor.Gender == Gender || confirmGenderMismatch, PatientCodes.GenderMismatchNotConfirmed, "The two files have different genders; confirm the merge to continue.", DomainFault.Conflict);
        var cleanedReason = (reason ?? string.Empty).Trim();
        Require(cleanedReason.Length is >= 1 and <= MaxReasonLength, PatientCodes.MergeReasonRequired, $"A merge needs a reason of 1 to {MaxReasonLength} characters.", DomainFault.Validation);

        Status = PatientStatus.Merged;
        MergedIntoPatientId = survivor.Id;
        MergedAtUtc = nowUtc;
        MergedByUserId = actorUserId;
        MergeReason = cleanedReason;
        Raise(new PatientsMerged(survivor.Id, survivor.Number, Id, Number, actorUserId, cleanedReason, nowUtc));
    }

    /// <summary>The age on <paramref name="today"/>, from the date of birth or the stated age (zero when neither is known).</summary>
    public PatientAge AgeOn(DateOnly today) => EstimatedDateOfBirth is { } birth ? PatientAge.Between(birth, today) : PatientAge.Zero;

    /// <summary>Refuses with PAT-020 unless the file is Active (INV-PAT-006).</summary>
    public void RequireActive(string action)
    {
        if (Status != PatientStatus.Active)
        {
            throw new InvalidStateTransitionException(PatientCodes.PatientMerged, "patient file", Status.ToString(), action);
        }
    }

    /// <summary>A mobile number as stored: separators removed, Arabic-Indic digits folded, optional <c>+</c> and 8–15 digits; <see langword="null"/> when blank; PAT-008 otherwise.</summary>
    public static string? CleanMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return null;
        }

        var value = FoldDigits(mobile).Where(character => character is not (' ' or '-' or '(' or ')' or '.' or '/')).ToArray();
        var digits = value.Length > 0 && value[0] == '+' ? value[1..] : value;
        Require(
            digits.Length is >= MinMobileDigits and <= MaxMobileDigits && digits.All(char.IsAsciiDigit),
            PatientCodes.MobileInvalid,
            $"The mobile number must have {MinMobileDigits} to {MaxMobileDigits} digits.",
            DomainFault.Validation);
        return new string(value);
    }

    /// <summary>
    /// The digits a mobile search looks for (validation v0.5.0 F-08): with the laboratory's national mobile length, a number
    /// typed with the trunk 0 (<c>0</c> + that many digits, e.g. <c>01019199912</c> in Egypt) is searched without it, so a
    /// "contains" match finds the file whether it was stored with the 0 or without it; any other text is searched as typed.
    /// </summary>
    public static string MobileSearchDigits(string digits, int? nationalLength)
    {
        ArgumentNullException.ThrowIfNull(digits);
        return nationalLength is { } length && length > 0 && digits.Length == length + 1 && digits[0] == '0' && digits[1] != '0' && digits.All(char.IsAsciiDigit)
            ? digits[1..]
            : digits;
    }

    /// <summary>A national id as stored: spaces and dashes removed, upper case, 5–32 letters or digits; <see langword="null"/> when blank; PAT-010 otherwise.</summary>
    public static string? CleanNationalId(string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(nationalId))
        {
            return null;
        }

        var value = new string(FoldDigits(nationalId).Where(character => character is not (' ' or '-')).ToArray()).ToUpperInvariant();
        Require(
            value.Length is >= MinNationalIdLength and <= MaxNationalIdLength && value.All(char.IsAsciiLetterOrDigit),
            PatientCodes.NationalIdInvalid,
            $"The national id must be {MinNationalIdLength} to {MaxNationalIdLength} letters or digits.",
            DomainFault.Validation);
        return value;
    }

    /// <summary>Arabic-Indic and Extended Arabic-Indic digits → ASCII digits.</summary>
    public static string FoldDigits(string value) =>
        new((value ?? string.Empty).Trim().Select(character => character switch
        {
            >= '\u0660' and <= '\u0669' => (char)('0' + (character - '\u0660')),
            >= '\u06F0' and <= '\u06F9' => (char)('0' + (character - '\u06F0')),
            _ => character,
        }).ToArray());

    private static Cleaned Clean(PatientDemographics input, PatientFieldRules rules, DateOnly today)
    {
        var fullName = CollapseSpaces(input.FullName);
        Require(fullName.Length is >= 1 and <= MaxNameLength, PatientCodes.NameInvalid, $"The name must be 1 to {MaxNameLength} characters.", DomainFault.Validation);
        var nameAr = string.IsNullOrWhiteSpace(input.NameAr) ? null : CollapseSpaces(input.NameAr);
        Require(nameAr is null || nameAr.Length <= MaxNameLength, PatientCodes.NameArInvalid, $"The Arabic name may not exceed {MaxNameLength} characters.", DomainFault.Validation);

        DateOnly? dateOfBirth = null;
        PatientAge? statedAge = null;
        DateOnly? ageRecordedOn = null;
        if (input.DateOfBirth is { } birth)
        {
            Require(birth >= EarliestDateOfBirth && birth <= today, PatientCodes.DateOfBirthInvalid, "The date of birth cannot be in the future or before 1900.", DomainFault.Validation);
            dateOfBirth = birth;
        }
        else if (input.Age is { } age)
        {
            statedAge = PatientAge.Require(age.Years, age.Months, age.Days);
            ageRecordedOn = today;
        }
        else
        {
            throw new DomainException(PatientCodes.AgeOrDateOfBirthRequired, "Enter a date of birth or an age.", DomainFault.Validation);
        }

        var mobile = CleanMobile(input.Mobile);
        Require(mobile is not null || !rules.MobileRequired, PatientCodes.MobileRequired, "The mobile number is required.", DomainFault.Validation);
        var nationalId = CleanNationalId(input.NationalId);
        Require(nationalId is not null || !rules.NationalIdRequired, PatientCodes.NationalIdRequired, "The national id is required.", DomainFault.Validation);

        return new Cleaned(
            input.Title,
            fullName,
            nameAr,
            input.Gender,
            dateOfBirth,
            statedAge,
            ageRecordedOn,
            input.PatientConditionId == Guid.Empty ? null : input.PatientConditionId,
            mobile,
            nationalId,
            OptionalText(input.Nationality, MaxNationalityLength),
            CleanEmail(input.Email),
            input.GovernorateId == Guid.Empty ? null : input.GovernorateId,
            OptionalText(input.City, MaxCityLength),
            OptionalText(input.Address, MaxAddressLength),
            OptionalText(input.Comments, MaxCommentsLength));
    }

    private void Apply(Cleaned value)
    {
        Title = value.Title;
        FullName = value.FullName;
        NameAr = value.NameAr;
        NameKey = PatientNameNormalizer.Normalize(value.FullName);
        NameArKey = PatientNameNormalizer.Normalize(value.NameAr);
        Gender = value.Gender;
        DateOfBirth = value.DateOfBirth;
        AgeYears = value.StatedAge?.Years;
        AgeMonths = value.StatedAge?.Months;
        AgeDays = value.StatedAge?.Days;
        AgeRecordedOn = value.AgeRecordedOn;
        PatientConditionId = value.PatientConditionId;
        Mobile = value.Mobile;
        NationalId = value.NationalId;
        Nationality = value.Nationality;
        Email = value.Email;
        GovernorateId = value.GovernorateId;
        City = value.City;
        Address = value.Address;
        Comments = value.Comments;
    }

    private Cleaned Current() => new(
        Title, FullName, NameAr, Gender, DateOfBirth, StatedAge, AgeRecordedOn, PatientConditionId, Mobile, NationalId, Nationality, Email, GovernorateId, City, Address, Comments);

    private static string? CleanEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var value = email.Trim();
        var at = value.IndexOf('@');
        var domain = at < 0 ? string.Empty : value[(at + 1)..];
        var valid = value.Length <= MaxEmailLength
                    && at is > 0 and <= 64
                    && value.IndexOf('@', at + 1) < 0
                    && !value.Any(char.IsWhiteSpace)
                    && domain.Contains('.', StringComparison.Ordinal)
                    && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.StartsWith('-');
        Require(valid, PatientCodes.EmailInvalid, "The e-mail address is not valid.", DomainFault.Validation);
        return value;
    }

    private static string? OptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        Require(trimmed.Length <= maxLength, PatientCodes.TextTooLong, $"A text field exceeds {maxLength} characters.", DomainFault.Validation);
        return trimmed;
    }

    private static string CollapseSpaces(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The validated demographics; record equality detects whether T2 changed anything.</summary>
    private sealed record Cleaned(
        PatientTitle Title,
        string FullName,
        string? NameAr,
        PatientGender Gender,
        DateOnly? DateOfBirth,
        PatientAge? StatedAge,
        DateOnly? AgeRecordedOn,
        Guid? PatientConditionId,
        string? Mobile,
        string? NationalId,
        string? Nationality,
        string? Email,
        Guid? GovernorateId,
        string? City,
        string? Address,
        string? Comments);
}
