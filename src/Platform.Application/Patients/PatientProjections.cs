using Platform.Contracts.Patients;
using Platform.Domain.Authorization;
using Platform.Domain.Patients;

namespace Platform.Application.Patients;

/// <summary>Permission keys the handlers consult beyond the request's own policy (field-level confidentiality, inactive content).</summary>
internal static class PatientPermissions
{
    public static readonly string ViewConfidential = PermissionCatalog.Key(PermissionCatalog.Patients, PermissionAction.ViewConfidential);

    public static readonly string ManageFiles = PermissionCatalog.Key(PermissionCatalog.Files, PermissionAction.Manage);
}

/// <summary>Projections of <see cref="PatientRow"/>: the age computed for <c>today</c> and the VIP flag masked for users without <c>patients.viewConfidential</c> (INV-PAT-007).</summary>
public static class PatientProjections
{
    /// <summary>The known date of birth, or the one implied by the stated age.</summary>
    public static DateOnly? BirthOf(PatientRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.DateOfBirth is { } birth)
        {
            return birth;
        }

        return row.AgeYears is { } years && row.AgeRecordedOn is { } on ? new PatientAge(years, row.AgeMonths ?? 0, row.AgeDays ?? 0).BirthDateOn(on) : null;
    }

    public static PatientAgeView AgeOf(PatientRow row, DateOnly today) =>
        View(BirthOf(row) is { } birth ? PatientAge.Between(birth, today) : PatientAge.Zero);

    public static PatientAgeView View(PatientAge age) => new(age.Years, age.Months, age.Days);

    /// <summary>
    /// The VIP identity mask of the lists (validation A-03, 2026-10-11; PAT intake Q8 and REG intake Q12 aligned, an
    /// assumption awaiting the business owner): for a user without the See-VIP special right the name, the Arabic name,
    /// the mobile and the national id of a VIP file are withheld in the patient register, its export, the patient picker
    /// and the similar-file check (listed there masked with its match, so no duplicate file is created: validation R-06),
    /// exactly as the visit register (CP-10) withholds them. Opening the file (and the printed
    /// documents) keeps the real identity: reception still registers the patient.
    /// </summary>
    public static bool IsIdentityMasked(PatientRow row, bool canSeeVip)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.IsVip && !canSeeVip;
    }

    /// <param name="visits">The file's visit figures from Registration (CP-11; UAT A-09); none when absent.</param>
    /// <param name="canSeeVip">The See-VIP special right; without it a VIP row is masked (<see cref="IsIdentityMasked"/>).</param>
    public static PatientListItem ToListItem(PatientRow row, DateOnly today, bool includeConfidential, PatientVisitFigures? visits = null, bool canSeeVip = true)
    {
        ArgumentNullException.ThrowIfNull(row);
        var figures = visits ?? PatientVisitFigures.None;
        var masked = IsIdentityMasked(row, canSeeVip);
        return new PatientListItem(
            row.Id,
            row.Number,
            row.Title.ToString(),
            masked ? string.Empty : row.FullName,
            masked ? null : row.NameAr,
            row.Gender.ToString(),
            BirthOf(row),
            row.DateOfBirth is null,
            AgeOf(row, today),
            masked ? null : row.Mobile,
            masked ? null : row.NationalId,
            row.PatientConditionId,
            includeConfidential ? row.IsVip : null,
            row.Status.ToString(),
            row.MergedIntoPatientId,
            row.MergedIntoNumber,
            row.RegisteredOn,
            figures.VisitCount,
            figures.LastVisitAtUtc,
            masked);
    }

    public static PatientDetail ToDetail(PatientRow row, DateOnly today, bool includeConfidential, string? mergedByName, IReadOnlyList<MergedPatientReference> mergedFiles)
    {
        ArgumentNullException.ThrowIfNull(row);
        var merge = row is { Status: PatientStatus.Merged, MergedIntoPatientId: { } survivorId, MergedAtUtc: { } mergedAt, MergedByUserId: { } mergedBy }
            ? new PatientMergeView(survivorId, row.MergedIntoNumber ?? string.Empty, mergedAt, mergedBy, mergedByName, row.MergeReason ?? string.Empty)
            : null;
        var stated = row.AgeYears is { } years ? new PatientAgeView(years, row.AgeMonths ?? 0, row.AgeDays ?? 0) : null;

        return new PatientDetail(
            row.Id,
            row.Number,
            row.Title.ToString(),
            row.FullName,
            row.NameAr,
            row.Gender.ToString(),
            BirthOf(row),
            row.DateOfBirth is null,
            AgeOf(row, today),
            stated,
            row.AgeRecordedOn,
            row.RegisteredOn,
            row.PatientConditionId,
            row.Mobile,
            row.NationalId,
            row.Nationality,
            row.Email,
            row.GovernorateId,
            row.City,
            row.Address,
            row.Comments,
            includeConfidential ? row.IsVip : null,
            row.Status.ToString(),
            merge,
            mergedFiles,
            row.CreatedAtUtc,
            row.CreatedByName,
            row.ModifiedAtUtc,
            row.ModifiedByName,
            row.RowVersion);
    }

    /// <param name="canSeeVip">The See-VIP special right; without it a VIP candidate is listed masked (<see cref="IsIdentityMasked"/>, validation R-06).</param>
    public static SimilarPatient ToSimilar(PatientRow row, DateOnly today, SimilarMatch match, bool canSeeVip = true)
    {
        ArgumentNullException.ThrowIfNull(row);
        var masked = IsIdentityMasked(row, canSeeVip);
        return new SimilarPatient(
            row.Id,
            row.Number,
            row.Title.ToString(),
            masked ? string.Empty : row.FullName,
            masked ? null : row.NameAr,
            row.Gender.ToString(),
            BirthOf(row),
            row.DateOfBirth is null,
            AgeOf(row, today),
            masked ? null : row.Mobile,
            masked ? null : row.NationalId,
            match.ToString(),
            masked);
    }

    public static PatientSummary ToSummary(PatientRow row, DateOnly today, Guid? survivorId)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new PatientSummary(
            row.Id, row.Number, row.Title.ToString(), row.FullName, row.NameAr, row.Gender.ToString(), BirthOf(row), row.DateOfBirth is null, AgeOf(row, today),
            row.PatientConditionId, row.IsVip, row.Mobile, row.NationalId, row.Status.ToString(), survivorId);
    }
}

/// <summary>How a candidate resembles the typed patient (BR-01), strongest first.</summary>
public enum SimilarMatch
{
    Exact = 0,
    Mobile = 1,
    Name = 2,
}

/// <summary>
/// The search box of the register (REG-002): a patient number (or part of it), mobile digits, a national id, or
/// name tokens in Latin or Arabic script (wildcards <c>*</c> / <c>%</c> are separators). Any of them matches.
/// </summary>
public sealed record PatientSearchTerms(string Number, string? Digits, string? NationalId, IReadOnlyList<string> NameTokens)
{
    public const int MaxLength = 200;

    public static PatientSearchTerms? Parse(string? text)
    {
        var raw = Patient.FoldDigits(text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        var upper = raw.ToUpperInvariant().Replace("*", string.Empty, StringComparison.Ordinal).Replace("%", string.Empty, StringComparison.Ordinal);
        var digitsOnly = new string(upper.Where(char.IsAsciiDigit).ToArray());
        var looksNumeric = upper.All(character => char.IsAsciiDigit(character) || character is ' ' or '+' or '-' or '(' or ')');
        var compact = new string(upper.Where(char.IsAsciiLetterOrDigit).ToArray());

        return new PatientSearchTerms(
            upper,
            looksNumeric && digitsOnly.Length >= 3 ? digitsOnly : null,
            compact.Length >= Patient.MinNationalIdLength ? compact : null,
            PatientNameNormalizer.Tokens(raw));
    }
}
