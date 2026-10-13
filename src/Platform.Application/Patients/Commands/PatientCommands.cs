using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Domain.Authorization;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Patients.Commands;

// Patient file transitions T1 (register) and T2 (update). Validators check shape; the aggregate decides (pitfall P-10).

[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.Create)]
public sealed record RegisterPatientCommand(
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    PatientAgeView? Age,
    Guid? PatientConditionId,
    string? Mobile,
    string? NationalId,
    string? Nationality,
    string? Email,
    Guid? GovernorateId,
    string? City,
    string? Address,
    string? Comments,
    bool? IsVip) : ICommand<CreatedPatient>;

public sealed class RegisterPatientValidator : AbstractValidator<RegisterPatientCommand>
{
    public RegisterPatientValidator()
    {
        RuleFor(command => command.Title).NotEmpty().IsEnumName(typeof(PatientTitle), caseSensitive: false);
        RuleFor(command => command.Gender).NotEmpty().IsEnumName(typeof(PatientGender), caseSensitive: false);
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(Patient.MaxNameLength);
        RuleFor(command => command.NameAr).MaximumLength(Patient.MaxNameLength);
        RuleFor(command => command.Age!).SetValidator(new PatientAgeValidator()).When(command => command.Age is not null);
        RuleFor(command => command.Mobile).MaximumLength(PatientShape.MaxRawMobileLength);
        RuleFor(command => command.NationalId).MaximumLength(PatientShape.MaxRawNationalIdLength);
        RuleFor(command => command.Nationality).MaximumLength(Patient.MaxNationalityLength);
        RuleFor(command => command.Email).MaximumLength(Patient.MaxEmailLength).EmailAddress().When(command => !string.IsNullOrWhiteSpace(command.Email));
        RuleFor(command => command.City).MaximumLength(Patient.MaxCityLength);
        RuleFor(command => command.Address).MaximumLength(Patient.MaxAddressLength);
        RuleFor(command => command.Comments).MaximumLength(Patient.MaxCommentsLength);
    }
}

public sealed class RegisterPatientHandler(
    IPatientRepository patients,
    IReferenceNumbers numbers,
    ITenantSettings settings,
    IUserPrivileges privileges,
    ILaboratoryTimeZone laboratoryZone,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RegisterPatientCommand, CreatedPatient>
{
    public async Task<CreatedPatient> Handle(RegisterPatientCommand command, CancellationToken cancellationToken)
    {
        if (command.IsVip is not null && !privileges.Has(PatientPermissions.ViewConfidential))
        {
            throw new DomainException(PatientCodes.VipRequiresConfidential, "Only users with the confidential right can set the VIP flag.", DomainFault.Refused);
        }

        var rules = await PatientShape.ReadRulesAsync(settings, cancellationToken);
        var demographics = PatientShape.Demographics(command.Title, command.FullName, command.NameAr, command.Gender, command.DateOfBirth, command.Age, command.PatientConditionId, command.Mobile, command.NationalId, command.Nationality, command.Email, command.GovernorateId, command.City, command.Address, command.Comments);
        var now = clock.UtcNow;
        var zone = await laboratoryZone.FindAsync(cancellationToken);   // the laboratory's day, not the UTC one (validation R-11)
        Patient.Check(demographics, rules, now, zone);   // refuse before drawing a number (numbers are never reused)

        var number = await numbers.NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, cancellationToken);
        var patient = Patient.Register(number, demographics, rules, command.IsVip ?? false, now, zone);
        patients.Add(patient);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedPatient(patient.Id, patient.Number);
    }
}

/// <summary>
/// T2. <paramref name="VisitId"/>: the visit the user edits from (Search registrations, CP-10) — the result-status lock is
/// judged on that visit; <see langword="null"/> from the patient file judges every visit of the patient (PAT-032).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.Edit)]
public sealed record UpdatePatientCommand(
    Guid PatientId,
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    PatientAgeView? Age,
    Guid? PatientConditionId,
    string? Mobile,
    string? NationalId,
    string? Nationality,
    string? Email,
    Guid? GovernorateId,
    string? City,
    string? Address,
    string? Comments,
    bool? IsVip,
    uint? RowVersion,
    Guid? VisitId = null) : ICommand;

public sealed class UpdatePatientValidator : AbstractValidator<UpdatePatientCommand>
{
    public UpdatePatientValidator()
    {
        RuleFor(command => command.PatientId).NotEmpty();
        RuleFor(command => command.Title).NotEmpty().IsEnumName(typeof(PatientTitle), caseSensitive: false);
        RuleFor(command => command.Gender).NotEmpty().IsEnumName(typeof(PatientGender), caseSensitive: false);
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(Patient.MaxNameLength);
        RuleFor(command => command.NameAr).MaximumLength(Patient.MaxNameLength);
        RuleFor(command => command.Age!).SetValidator(new PatientAgeValidator()).When(command => command.Age is not null);
        RuleFor(command => command.Mobile).MaximumLength(PatientShape.MaxRawMobileLength);
        RuleFor(command => command.NationalId).MaximumLength(PatientShape.MaxRawNationalIdLength);
        RuleFor(command => command.Nationality).MaximumLength(Patient.MaxNationalityLength);
        RuleFor(command => command.Email).MaximumLength(Patient.MaxEmailLength).EmailAddress().When(command => !string.IsNullOrWhiteSpace(command.Email));
        RuleFor(command => command.City).MaximumLength(Patient.MaxCityLength);
        RuleFor(command => command.Address).MaximumLength(Patient.MaxAddressLength);
        RuleFor(command => command.Comments).MaximumLength(Patient.MaxCommentsLength);
    }
}

/// <summary>
/// T2 with the demographics lock (change request 2026-10-13 item 1): an <b>identity</b> field (title, names, gender, date of
/// birth or age, condition, national id, nationality) changes only while the highest live result status of the visit (or of
/// the patient, from the file) is within the member's <c>DemographicsEditUpTo</c> special right; otherwise PAT-032 (403).
/// Contact fields (mobile, e-mail, governorate, city, address, comments) and the VIP flag need <c>patients.edit</c> alone.
/// The field history is the ordinary patient update in <c>audit.field_changes</c>.
/// </summary>
public sealed class UpdatePatientHandler(
    IPatientRepository patients,
    ITenantSettings settings,
    IUserPrivileges privileges,
    IPatientResultStatusLookup results,
    ILaboratoryTimeZone laboratoryZone,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdatePatientCommand>
{
    public async Task Handle(UpdatePatientCommand command, CancellationToken cancellationToken)
    {
        var patient = await patients.GetForUpdateAsync(command.PatientId, cancellationToken)
            ?? throw new DomainException(PatientCodes.PatientNotFound, "Patient file not found.", DomainFault.NotFound);

        // Optimistic lock on the edit the user saw (the xmin token covers the race after this point).
        if (command.RowVersion is { } expected && expected != patient.RowVersion)
        {
            throw new DomainException(ProblemCodes.ConcurrencyConflict, "The patient file was changed by someone else; reload it and try again.", DomainFault.Conflict);
        }

        if (command.IsVip is not null && !privileges.Has(PatientPermissions.ViewConfidential))
        {
            throw new DomainException(PatientCodes.VipRequiresConfidential, "Only users with the confidential right can change the VIP flag.", DomainFault.Refused);
        }

        var rules = await PatientShape.ReadRulesAsync(settings, cancellationToken);
        var demographics = PatientShape.Demographics(command.Title, command.FullName, command.NameAr, command.Gender, command.DateOfBirth, command.Age, command.PatientConditionId, command.Mobile, command.NationalId, command.Nationality, command.Email, command.GovernorateId, command.City, command.Address, command.Comments);
        var now = clock.UtcNow;
        var zone = await laboratoryZone.FindAsync(cancellationToken);

        // The lock is judged only when an identity field actually changes: a contact-only edit never reads the results.
        if (patient.IdentityChangesWith(demographics, rules, now, zone))
        {
            var highest = await results.HighestStatusAsync(patient.Id, command.VisitId, cancellationToken);
            if (!privileges.SpecialRights.AllowsDemographicsEditAt(highest))
            {
                throw new DomainException(PatientCodes.DemographicsLockedByResults, DemographicsLock.Message(highest, privileges.SpecialRights.DemographicsEditUpTo), DomainFault.Refused);
            }
        }

        patient.Update(demographics, rules, command.IsVip, now, zone);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The wording of the demographics lock (PAT-032); the UI translates the statuses itself from the lock query.</summary>
internal static class DemographicsLock
{
    public static string Message(DemographicsEditLimit highest, DemographicsEditLimit limit) =>
        $"The patient's identity can no longer change: a result is {highest} and your right allows identity changes up to {limit}. Contact details can still be edited.";
}

public sealed class PatientAgeValidator : AbstractValidator<PatientAgeView>
{
    public PatientAgeValidator()
    {
        RuleFor(age => age.Years).InclusiveBetween(0, PatientAge.MaxYears);
        RuleFor(age => age.Months).InclusiveBetween(0, 11);
        RuleFor(age => age.Days).InclusiveBetween(0, 31);
    }
}

/// <summary>Request shape → domain values, and the tenant parameters of the patient form (intake §13).</summary>
internal static class PatientShape
{
    /// <summary>Raw input bounds (separators allowed); the aggregate enforces the cleaned bounds.</summary>
    public const int MaxRawMobileLength = 32;
    public const int MaxRawNationalIdLength = 64;

    public static PatientDemographics Demographics(
        string title, string fullName, string? nameAr, string gender, DateOnly? dateOfBirth, PatientAgeView? age, Guid? conditionId, string? mobile,
        string? nationalId, string? nationality, string? email, Guid? governorateId, string? city, string? address, string? comments) =>
        new(
            Enum.Parse<PatientTitle>(title, ignoreCase: true),
            fullName,
            nameAr,
            Enum.Parse<PatientGender>(gender, ignoreCase: true),
            dateOfBirth,
            age is null ? null : new PatientAge(age.Years, age.Months, age.Days),
            conditionId,
            mobile,
            nationalId,
            nationality,
            email,
            governorateId,
            city,
            address,
            comments);

    public static async Task<PatientFieldRules> ReadRulesAsync(ITenantSettings settings, CancellationToken cancellationToken) =>
        new(
            await settings.GetAsync(SettingKeys.MandatoryMobile, cancellationToken),
            await settings.GetAsync(SettingKeys.MandatoryNationalId, cancellationToken));
}

/// <summary>The authenticated actor of a command, or the coded refusal (01 §3.3).</summary>
internal static class PatientActor
{
    public static Guid Of(ICurrentUser user) =>
        user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
}
