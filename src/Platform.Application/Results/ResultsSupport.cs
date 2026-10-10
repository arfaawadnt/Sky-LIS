using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Services;
using Platform.Application.Organization;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Results;
using Platform.SharedKernel.Primitives;
using CatalogueAge = Platform.Domain.Catalogue.PatientAge;

namespace Platform.Application.Results;

/// <summary>The acting user of a results command; an unauthenticated scope is refused (the policy attribute already requires a session).</summary>
internal static class ResultsActor
{
    public static Guid Of(ICurrentUser user) =>
        user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);

    /// <summary>The signature name (BR-09): the member's full name in the tenant, else the platform display name.</summary>
    public static async Task<string?> NameOfAsync(ICurrentUser user, IUserDirectory directory, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } id)
        {
            return user.DisplayName;
        }

        var names = await directory.GetNamesAsync([id], cancellationToken);
        if (names.TryGetValue(id, out var name))
        {
            return string.IsNullOrWhiteSpace(name.FullNameEn) ? name.DisplayName : name.FullNameEn;
        }

        return user.DisplayName;
    }
}

/// <summary>Coded "not found" refusals of the module.</summary>
internal static class ResultsNotFound
{
    public static DomainException Result() => new(ResultsCodes.ResultNotFound, "Result not found.", DomainFault.NotFound);

    public static DomainException Unit() => new(ResultsCodes.ReportUnitNotFound, "Report not found.", DomainFault.NotFound);

    public static DomainException PrintList() => new(ResultsCodes.PrintListNotFound, "Print list not found.", DomainFault.NotFound);

    public static DomainException Sample() => new(ResultsCodes.SampleNotFound, "No sample carries this number.", DomainFault.NotFound);

    public static DomainException Visit() => new(ResultsCodes.VisitNotFound, "No results were found for this visit.", DomainFault.NotFound);
}

/// <summary>Result rights (CP-37; LDM §8 S R V E P) of the current user, for one result or as a visibility filter.</summary>
public static class ResultRights
{
    public static ResultRightsGrant For(IUserPrivileges privileges, Guid clinicalGroupId, Guid branchId)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        var special = privileges.SpecialRights;
        return new ResultRightsGrant(
            privileges.CanOnResults(ResultAction.See, clinicalGroupId, branchId),
            privileges.CanOnResults(ResultAction.Review, clinicalGroupId, branchId),
            privileges.CanOnResults(ResultAction.Verify, clinicalGroupId, branchId),
            privileges.CanOnResults(ResultAction.Edit, clinicalGroupId, branchId),
            privileges.CanOnResults(ResultAction.Print, clinicalGroupId, branchId),
            special.CanModifyVerifiedResult,
            special.CanPreviewWithoutVerify);
    }

    public static ResultActor Actor(Guid userId, string? name, IUserPrivileges privileges, Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ResultActor(userId, name, For(privileges, result.ClinicalGroupId, result.BranchId));
    }

    /// <summary>The "see" rights as a filter: a right without a branch covers every branch.</summary>
    public static ResultVisibility VisibilityOf(IUserPrivileges privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        var seeing = privileges.ResultRights.Where(right => right.Normalised().CanSee).ToList();
        var everywhere = seeing.Where(right => right.BranchId is null).Select(right => right.ClinicalGroupId).Distinct().ToList();
        var byBranch = seeing.Where(right => right.BranchId is not null)
            .GroupBy(right => right.BranchId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(right => right.ClinicalGroupId).Distinct().ToList());
        return new ResultVisibility(false, everywhere, byBranch);
    }

    public static ReportUnitRightsView View(ResultRightsGrant rights) =>
        new(rights.CanEdit, rights.CanReview, rights.CanVerify, rights.CanPrint, rights.CanModifyVerified, rights.CanPreviewWithoutVerify);
}

/// <summary>What BR-08 needs of the patient: sex, age (calendar units) and condition.</summary>
public sealed record PatientProfile(GenderRestriction Sex, CatalogueAge Age, Guid? ConditionId)
{
    public static PatientProfile Unknown { get; } = new(GenderRestriction.Any, CatalogueAge.FromDays(0), null);

    public static PatientProfile From(PatientSummary? summary, DateOnly today)
    {
        if (summary is null)
        {
            return Unknown;
        }

        var sex = summary.Gender switch
        {
            "Male" => GenderRestriction.Male,
            "Female" => GenderRestriction.Female,
            _ => GenderRestriction.Any,
        };

        CatalogueAge age;
        if (summary.DateOfBirth is { } birth && birth <= today)
        {
            age = CatalogueAge.Between(birth, today);
        }
        else
        {
            var days = (int)Math.Round((summary.Age.Years * CatalogueAge.DaysPerYear) + (summary.Age.Months * CatalogueAge.DaysPerMonth) + summary.Age.Days);
            age = CatalogueAge.FromDays(Math.Max(0, days));
        }

        return new PatientProfile(sex, age, summary.PatientConditionId is { } condition && condition != Guid.Empty ? condition : null);
    }
}

/// <summary>The catalogue definition of a test as entry and display need it.</summary>
public static class ResultCatalogue
{
    public static ResultTestRules Rules(CatalogueTestInfo test)
    {
        ArgumentNullException.ThrowIfNull(test);
        var options = test.ListValues.OrderBy(value => value.DisplayOrder).Select(value => new ResultListOption(value.Value, value.IsNormal, value.IsCritical, value.IsPlaceholder)).ToList();
        return ResultTestRules.For(test.Type, test.Decimals, test.UnitName, options, test.Timing.DeltaPercent);
    }

    /// <summary>BR-08: the band for the patient through the catalogue's resolver, as the range stored on the result.</summary>
    public static ResolvedRange? Range(CatalogueTestInfo test, PatientProfile patient)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(patient);
        var band = ReferenceRangeResolver.Instance.Resolve(test.RangeBands, patient.Sex, patient.Age, patient.ConditionId);
        return band is null ? null : ResolvedRange.Create(band.Low, band.High, band.PanicLow, band.PanicHigh, band.ReportableLow, band.ReportableHigh, band.TextRange);
    }

    /// <summary>The start of the delta window (BR-11): the test's window, or no limit when empty (intake §16 Q4).</summary>
    public static DateTimeOffset? DeltaSince(CatalogueTestInfo test, DateTimeOffset nowUtc) =>
        test.Timing.DeltaWindowDays is { } days && days > 0 ? nowUtc.AddDays(-days) : null;

    public static RangeView? RangeView(ResolvedRange? range, int decimals) =>
        range is null ? null : new RangeView(range.Low, range.High, range.PanicLow, range.PanicHigh, range.ReportableLow, range.ReportableHigh, range.Text, range.Display(decimals));
}

/// <summary>
/// T2b: recomputes the formula results of a visit from the values of the tests they reference (CP-19 "formulas
/// compute automatically", LDM §3.3) after inputs changed; chained formulas settle in at most five passes.
/// </summary>
internal static class FormulaRecalculator
{
    private const int MaxPasses = 5;

    public static async Task<IReadOnlyList<Guid>> RecomputeAsync(
        IReadOnlyList<Result> visitResults,
        IReadOnlyDictionary<Guid, CatalogueTestInfo> tests,
        IFormulaEvaluator evaluator,
        PatientProfile patient,
        Func<Result, CatalogueTestInfo, Task<PreviousResultValue?>> previous,
        ResultSource source,
        Guid? actorUserId,
        string? actorName,
        DateTimeOffset nowUtc)
    {
        var formulas = visitResults
            .Where(result => result.IsLive && result.TestType == LabTestType.Formula && tests.TryGetValue(result.TestId, out var test) && !string.IsNullOrWhiteSpace(test.FormulaExpression))
            .ToList();
        if (formulas.Count == 0)
        {
            return [];
        }

        var changed = new List<Guid>();
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var any = false;
            foreach (var formula in formulas)
            {
                var test = tests[formula.TestId];
                var values = InputValues(visitResults, tests, formula, test.FormulaReferences);
                var evaluation = evaluator.Evaluate(test.FormulaExpression!, values);
                if (!evaluation.HasValue)
                {
                    continue;
                }

                var rules = ResultCatalogue.Rules(test);
                var prior = rules.DeltaPercent is null ? null : await previous(formula, test);
                if (formula.ApplyComputed(evaluation.Value, rules, ResultCatalogue.Range(test, patient), prior, source, actorUserId, actorName, nowUtc))
                {
                    any = true;
                    if (!changed.Contains(formula.Id))
                    {
                        changed.Add(formula.Id);
                    }
                }
            }

            if (!any)
            {
                break;
            }
        }

        return changed;
    }

    /// <summary>The value of each referenced code: the live result of that test in the formula's unit, else elsewhere in the visit.</summary>
    private static Dictionary<string, decimal?> InputValues(IReadOnlyList<Result> visitResults, IReadOnlyDictionary<Guid, CatalogueTestInfo> tests, Result formula, IReadOnlyList<string> references)
    {
        var values = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in references)
        {
            var candidates = visitResults
                .Where(result => result.IsLive && result.Id != formula.Id && tests.TryGetValue(result.TestId, out var test) && string.Equals(test.Code, code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(result => result.ReportUnitKey == formula.ReportUnitKey)
                .ThenByDescending(result => result.NumericValue is not null)
                .ToList();
            values[code] = candidates.FirstOrDefault()?.NumericValue;
        }

        return values;
    }
}
