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

    /// <param name="visits">The file's visit figures from Registration (CP-11; UAT A-09); none when absent.</param>
    public static PatientListItem ToListItem(PatientRow row, DateOnly today, bool includeConfidential, PatientVisitFigures? visits = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        var figures = visits ?? PatientVisitFigures.None;
        return new PatientListItem(
            row.Id,
            row.Number,
            row.Title.ToString(),
            row.FullName,
            row.NameAr,
            row.Gender.ToString(),
            BirthOf(row),
            row.DateOfBirth is null,
            AgeOf(row, today),
            row.Mobile,
            row.NationalId,
            row.PatientConditionId,
            includeConfidential ? row.IsVip : null,
            row.Status.ToString(),
            row.MergedIntoPatientId,
            row.MergedIntoNumber,
            row.RegisteredOn,
            figures.VisitCount,
            figures.LastVisitAtUtc);
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

    public static SimilarPatient ToSimilar(PatientRow row, DateOnly today, SimilarMatch match)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new SimilarPatient(row.Id, row.Number, row.Title.ToString(), row.FullName, row.NameAr, row.Gender.ToString(), BirthOf(row), row.DateOfBirth is null, AgeOf(row, today), row.Mobile, row.NationalId, match.ToString());
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
