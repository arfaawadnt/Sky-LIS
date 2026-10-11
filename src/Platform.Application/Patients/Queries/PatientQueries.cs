using System.Globalization;
using System.Text;
using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Domain.Authorization;
using Platform.Domain.Patients;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Patients.Queries;

// Registers of the module (intake §14). Queries carry a policy like commands (D-04) and read projections only.

/// <summary><paramref name="Q"/>: number, name, mobile or national id; <paramref name="Vip"/> needs <c>patients.viewConfidential</c> (PAT-031).</summary>
[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.View)]
public sealed record ListPatientsQuery(string? Q, string? Gender, bool? Vip, string? Status, int? Page, int? PageSize) : IQuery<Paged<PatientListItem>>;

public sealed class ListPatientsValidator : AbstractValidator<ListPatientsQuery>
{
    public ListPatientsValidator()
    {
        RuleFor(query => query.Q).MaximumLength(PatientSearchTerms.MaxLength);
        RuleFor(query => query.Gender).IsEnumName(typeof(PatientGender), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Gender));
        RuleFor(query => query.Status).IsEnumName(typeof(PatientStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

/// <summary>The register page with each file's visit count and last visit from Registration (CP-11; UAT A-09).</summary>
public sealed class ListPatientsHandler(IPatientRepository patients, IPatientVisitStatistics visits, IUserPrivileges privileges, IClock clock) : IQueryHandler<ListPatientsQuery, Paged<PatientListItem>>
{
    public async Task<Paged<PatientListItem>> Handle(ListPatientsQuery query, CancellationToken cancellationToken)
    {
        var confidential = privileges.Has(PatientPermissions.ViewConfidential);
        var filter = PatientFilters.From(query.Q, query.Gender, query.Vip, query.Status, confidential);
        var page = await patients.SearchAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var figures = await visits.ForPatientsAsync([.. page.Items.Select(row => row.Id)], cancellationToken);
        return new Paged<PatientListItem>(
            page.Items.Select(row => PatientProjections.ToListItem(row, today, confidential, figures.GetValueOrDefault(row.Id))).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.View)]
public sealed record GetPatientQuery(Guid PatientId) : IQuery<PatientDetail>;

public sealed class GetPatientHandler(IPatientRepository patients, IUserPrivileges privileges, IClock clock) : IQueryHandler<GetPatientQuery, PatientDetail>
{
    public async Task<PatientDetail> Handle(GetPatientQuery query, CancellationToken cancellationToken)
    {
        var row = await patients.GetRowAsync(query.PatientId, cancellationToken)
            ?? throw new DomainException(PatientCodes.PatientNotFound, "Patient file not found.", DomainFault.NotFound);
        var mergedFiles = await patients.ListMergedIntoAsync(row.Id, cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // The merger's display name as of the action is the modifier stamp of the merge (the last change of a frozen file).
        var mergedByName = row.Status == PatientStatus.Merged ? row.ModifiedByName : null;
        return PatientProjections.ToDetail(row, today, privileges.Has(PatientPermissions.ViewConfidential), mergedByName, mergedFiles);
    }
}

/// <summary>Similar existing files while the clerk types (BR-01, REG-002): always answered; the setting tells the UI whether to warn.</summary>
[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.View)]
public sealed record FindSimilarPatientsQuery(string? Name, string? Mobile) : IQuery<SimilarPatientsResponse>;

public sealed class FindSimilarPatientsValidator : AbstractValidator<FindSimilarPatientsQuery>
{
    public FindSimilarPatientsValidator()
    {
        RuleFor(query => query.Name).MaximumLength(Patient.MaxNameLength);
        RuleFor(query => query.Mobile).MaximumLength(32);
    }
}

public sealed class FindSimilarPatientsHandler(IPatientRepository patients, ITenantSettings settings, IClock clock) : IQueryHandler<FindSimilarPatientsQuery, SimilarPatientsResponse>
{
    public const int MaxCandidates = 10;
    private const int PreFilterLimit = 200;

    public async Task<SimilarPatientsResponse> Handle(FindSimilarPatientsQuery query, CancellationToken cancellationToken)
    {
        var warn = await settings.GetAsync(SettingKeys.SimilarNameWarning, cancellationToken);
        var key = PatientNameNormalizer.Normalize(query.Name);
        string[] tokens = key.Length == 0 ? [] : key.Split(' ');
        var mobile = TryCleanMobile(query.Mobile);
        if (tokens.Length == 0 && mobile is null)
        {
            return new SimilarPatientsResponse(warn, []);
        }

        var rows = await patients.FindSimilarCandidatesAsync(tokens, mobile, PreFilterLimit, cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var candidates = rows
            .Select(row => (Row: row, Match: Classify(key, mobile, row)))
            .Where(candidate => candidate.Match is not null)
            .OrderBy(candidate => candidate.Match)
            .ThenByDescending(candidate => candidate.Row.CreatedAtUtc)
            .Take(MaxCandidates)
            .Select(candidate => PatientProjections.ToSimilar(candidate.Row, today, candidate.Match!.Value))
            .ToList();
        return new SimilarPatientsResponse(warn, candidates);
    }

    /// <summary>Exact = similar name and the same mobile; Mobile = the same mobile; Name = a similar Latin or Arabic name.</summary>
    public static SimilarMatch? Classify(string queryKey, string? mobile, PatientRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var byName = PatientNameNormalizer.KeysMatch(queryKey, row.NameKey) || PatientNameNormalizer.KeysMatch(queryKey, row.NameArKey);
        var byMobile = mobile is not null && string.Equals(mobile, row.Mobile, StringComparison.Ordinal);
        return (byName, byMobile) switch
        {
            (true, true) => SimilarMatch.Exact,
            (false, true) => SimilarMatch.Mobile,
            (true, false) => SimilarMatch.Name,
            _ => null,
        };
    }

    /// <summary>A half-typed mobile is no refusal here: it simply does not take part in the match.</summary>
    private static string? TryCleanMobile(string? mobile)
    {
        try
        {
            return Patient.CleanMobile(mobile);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.View)]
public sealed record GetPatientFormSettingsQuery : IQuery<PatientFormSettings>;

public sealed class GetPatientFormSettingsHandler(ITenantSettings settings) : IQueryHandler<GetPatientFormSettingsQuery, PatientFormSettings>
{
    public async Task<PatientFormSettings> Handle(GetPatientFormSettingsQuery query, CancellationToken cancellationToken) =>
        new(
            await settings.GetAsync(SettingKeys.MandatoryMobile, cancellationToken),
            await settings.GetAsync(SettingKeys.MandatoryNationalId, cancellationToken),
            await settings.GetAsync(SettingKeys.SimilarNameWarning, cancellationToken));
}

/// <summary>A generated file (CSV) the controller streams with its name and type.</summary>
public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>CSV export of the register (intake §14): PII leaves the system, so it has its own permission; at most <see cref="ExportPatientsHandler.MaxRows"/> rows (PAT-050).</summary>
[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.Export)]
public sealed record ExportPatientsQuery(string? Q, string? Gender, bool? Vip, string? Status) : IQuery<ExportFile>;

public sealed class ExportPatientsValidator : AbstractValidator<ExportPatientsQuery>
{
    public ExportPatientsValidator()
    {
        RuleFor(query => query.Q).MaximumLength(PatientSearchTerms.MaxLength);
        RuleFor(query => query.Gender).IsEnumName(typeof(PatientGender), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Gender));
        RuleFor(query => query.Status).IsEnumName(typeof(PatientStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
    }
}

public sealed class ExportPatientsHandler(IPatientRepository patients, IUserPrivileges privileges, IClock clock) : IQueryHandler<ExportPatientsQuery, ExportFile>
{
    public const int MaxRows = 10_000;

    public async Task<ExportFile> Handle(ExportPatientsQuery query, CancellationToken cancellationToken)
    {
        var confidential = privileges.Has(PatientPermissions.ViewConfidential);
        var filter = PatientFilters.From(query.Q, query.Gender, query.Vip, query.Status, confidential);
        var rows = await patients.SearchAllAsync(filter, MaxRows + 1, cancellationToken);
        if (rows.Count > MaxRows)
        {
            throw new DomainException(PatientCodes.ExportTooLarge, $"The export is limited to {MaxRows} rows; narrow the filter.", DomainFault.Validation);
        }

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var csv = new StringBuilder();
        var header = new List<string> { "Number", "Title", "Name", "Arabic name", "Gender", "Date of birth", "Estimated", "Age (years)", "Mobile", "National id", "Nationality", "E-mail", "City", "Address", "Status", "Merged into", "Registered on" };
        if (confidential)
        {
            header.Add("VIP");
        }

        AppendLine(csv, header);
        foreach (var row in rows)
        {
            var cells = new List<string?>
            {
                row.Number,
                row.Title.ToString(),
                row.FullName,
                row.NameAr,
                row.Gender.ToString(),
                PatientProjections.BirthOf(row)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.DateOfBirth is null ? "yes" : "no",
                PatientProjections.AgeOf(row, today).Years.ToString(CultureInfo.InvariantCulture),
                row.Mobile,
                row.NationalId,
                row.Nationality,
                row.Email,
                row.City,
                row.Address,
                row.Status.ToString(),
                row.MergedIntoNumber,
                row.RegisteredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
            if (confidential)
            {
                cells.Add(row.IsVip ? "yes" : "no");
            }

            AppendLine(csv, cells);
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();   // BOM: spreadsheet tools read Arabic correctly
        return new ExportFile($"patients-{now.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv", "text/csv; charset=utf-8", bytes);
    }

    /// <summary>RFC 4180 quoting plus formula neutralisation (a cell starting with = + - @ is prefixed with an apostrophe).</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var safe = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }

    private static void AppendLine(StringBuilder csv, IEnumerable<string?> cells) => csv.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
}

/// <summary>The register filter from query parameters; the VIP filter is refused without the confidential right (PAT-031).</summary>
internal static class PatientFilters
{
    public static PatientSearchFilter From(string? q, string? gender, bool? vip, string? status, bool confidential)
    {
        if (vip is not null && !confidential)
        {
            throw new DomainException(PatientCodes.VipFilterRequiresConfidential, "Only users with the confidential right can filter by VIP.", DomainFault.Refused);
        }

        return new PatientSearchFilter(
            PatientSearchTerms.Parse(q),
            string.IsNullOrEmpty(gender) ? null : Enum.Parse<PatientGender>(gender, ignoreCase: true),
            vip,
            string.IsNullOrEmpty(status) ? null : Enum.Parse<PatientStatus>(status, ignoreCase: true));
    }
}
