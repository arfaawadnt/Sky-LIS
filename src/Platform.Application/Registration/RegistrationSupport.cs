using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Patients;
using Platform.Application.Pricing;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Contracts.Pricing;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.Domain.Settings;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration;

/// <summary>The authenticated actor of a Registration command, or the coded refusal (01 §3.3).</summary>
internal static class RegistrationActor
{
    public static Guid Of(ICurrentUser user) =>
        user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
}

/// <summary>The cross-module checks every Registration handler shares (intake §7 preconditions); each refusal carries its REG code.</summary>
internal static class RegistrationRules
{
    public const string PatientMergedStatus = "Merged";

    public static async Task<PatientSummary> RequirePatientAsync(IPatientLookup patients, Guid patientId, CancellationToken cancellationToken)
    {
        var patient = await patients.GetAsync(patientId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.PatientNotFound, "The patient file does not exist.", DomainFault.Validation);
        if (string.Equals(patient.Status, PatientMergedStatus, StringComparison.Ordinal))
        {
            throw new DomainException(RegistrationCodes.PatientMerged, "The patient file was merged into another file; register on the surviving file.", DomainFault.Validation);
        }

        return patient;
    }

    /// <summary>Tenant parameters mandatoryMobile / mandatoryNationalId (CP-57) applied to the chosen file.</summary>
    public static async Task CheckPatientSettingsAsync(ITenantSettings settings, PatientSummary patient, CancellationToken cancellationToken)
    {
        if (await settings.GetAsync(SettingKeys.MandatoryMobile, cancellationToken) && string.IsNullOrWhiteSpace(patient.Mobile))
        {
            throw new DomainException(RegistrationCodes.MobileRequired, "The patient's mobile number is required; update the patient file first.", DomainFault.Validation);
        }

        if (await settings.GetAsync(SettingKeys.MandatoryNationalId, cancellationToken) && string.IsNullOrWhiteSpace(patient.NationalId))
        {
            throw new DomainException(RegistrationCodes.NationalIdRequired, "The patient's national id is required; update the patient file first.", DomainFault.Validation);
        }
    }

    public static async Task<(PayerSummary Payer, ContractTierSummary Tier)> RequireContractAsync(IPayerLookup payers, Guid payerId, Guid tierId, CancellationToken cancellationToken)
    {
        var payer = await payers.FindPayerAsync(payerId, cancellationToken);
        if (payer is null || !payer.IsActive)
        {
            throw new DomainException(RegistrationCodes.PayerInvalid, "The payer does not exist or is inactive.", DomainFault.Validation);
        }

        var tier = await payers.FindTierAsync(tierId, cancellationToken);
        if (tier is null || tier.PayerId != payerId || !tier.IsActive)
        {
            throw new DomainException(RegistrationCodes.TierInvalid, "The contract tier does not belong to the payer or is inactive.", DomainFault.Validation);
        }

        return (payer, tier);
    }

    public static async Task CheckDoctorAsync(IDoctorLookup doctors, ITenantSettings settings, Guid? doctorId, Guid branchId, CancellationToken cancellationToken)
    {
        if (doctorId is not { } id || id == Guid.Empty)
        {
            if (await settings.GetAsync(SettingKeys.MandatoryDoctor, cancellationToken))
            {
                throw new DomainException(RegistrationCodes.DoctorRequired, "The referring doctor is required.", DomainFault.Validation);
            }

            return;
        }

        if (!await doctors.IsSelectableAsync(id, branchId, cancellationToken))
        {
            throw new DomainException(RegistrationCodes.DoctorNotSelectable, "The doctor is inactive or not available in this branch.", DomainFault.Validation);
        }
    }

    public static async Task CheckDiagnosisAsync(IOrderRulesLookup rules, Guid? diagnosisId, CancellationToken cancellationToken)
    {
        if (diagnosisId is { } id && await rules.DiagnosisAsync(id, cancellationToken) is null)
        {
            throw new DomainException(RegistrationCodes.DiagnosisInvalid, "The diagnosis does not exist or is inactive.", DomainFault.Validation);
        }
    }

    /// <summary>The discount request of V1/V6 with the catalogue reason and the actor's cap (INV-REG-005).</summary>
    public static async Task<DiscountRequest?> DiscountAsync(IOrderRulesLookup rules, ITenantSettings settings, IUserPrivileges privileges, VisitDiscountRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return null;
        }

        if (request.ReasonId == Guid.Empty)
        {
            throw new DomainException(RegistrationCodes.DiscountReasonRequired, "A discount needs a reason.", DomainFault.Validation);
        }

        var reason = await rules.ReasonAsync(request.ReasonId, ReasonKind.Discount, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.DiscountReasonInvalid, "The discount reason does not exist, is inactive or is not a discount reason.", DomainFault.Validation);
        var kind = Enum.Parse<DiscountKind>(request.Kind, ignoreCase: true);
        var cap = privileges.SpecialRights.EffectiveDiscountCap(await settings.GetAsync(SettingKeys.DiscountCapPercent, cancellationToken));
        return new DiscountRequest(kind, request.Value, reason.ReasonId, reason.NameEn, cap);
    }

    /// <summary>A reason id of <paramref name="kind"/> or a free text (one of them); the stored text is the reason's name with the note.</summary>
    public static async Task<string> ReasonTextAsync(IOrderRulesLookup rules, Guid? reasonId, string? text, ReasonKind kind, string requiredCode, CancellationToken cancellationToken)
    {
        var note = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (reasonId is { } id && id != Guid.Empty)
        {
            var reason = await rules.ReasonAsync(id, kind, cancellationToken)
                ?? throw new DomainException(RegistrationCodes.ReasonInvalid, $"The reason does not exist, is inactive or is not a {kind.ToString().ToLowerInvariant()} reason.", DomainFault.Validation);
            var combined = note is null ? reason.NameEn : $"{reason.NameEn} — {note}";
            return combined.Length <= Visit.MaxReasonLength ? combined : combined[..Visit.MaxReasonLength];
        }

        return note ?? throw new DomainException(requiredCode, "A reason is required.", DomainFault.Validation);
    }

    /// <summary>Preparation answers checked against the questions that apply (REG-016); blank answers are skipped.</summary>
    public static IReadOnlyList<PreparationAnswerInput> Answers(IReadOnlyList<PreparationAnswerRequest>? answers, IReadOnlyList<PreparationQuestion> applicable)
    {
        var result = new List<PreparationAnswerInput>();
        foreach (var answer in answers ?? [])
        {
            var text = (answer.Answer ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var question = applicable.FirstOrDefault(candidate => candidate.PreparationId == answer.PreparationId)
                ?? throw new DomainException(RegistrationCodes.PreparationInvalid, "The preparation question does not apply to the ordered services.", DomainFault.Validation);
            text = question.AnswerType switch
            {
                PreparationAnswerType.YesNo => text.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "Yes"
                    : text.Equals("no", StringComparison.OrdinalIgnoreCase) ? "No"
                    : throw new DomainException(RegistrationCodes.PreparationInvalid, $"Answer '{question.Code}' with Yes or No.", DomainFault.Validation),
                PreparationAnswerType.OptionList => question.Options.FirstOrDefault(option => string.Equals(option, text, StringComparison.OrdinalIgnoreCase))
                    ?? throw new DomainException(RegistrationCodes.PreparationInvalid, $"Answer '{question.Code}' with one of its options.", DomainFault.Validation),
                _ => text.Length <= VisitPreparationAnswer.MaxAnswerLength ? text
                    : throw new DomainException(RegistrationCodes.PreparationInvalid, $"An answer has at most {VisitPreparationAnswer.MaxAnswerLength} characters.", DomainFault.Validation),
            };
            result.Add(new PreparationAnswerInput(question.PreparationId, question.QuestionEn, question.QuestionAr, text));
        }

        return result;
    }

    public static async Task<SampleNumberSource> SampleNumbersAsync(IReferenceNumbers numbers, int count, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var issued = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            issued.Add(await numbers.NextAsync(Visit.SampleKind, Visit.SamplePrefix, nowUtc, cancellationToken));
        }

        return new SampleNumberSource(issued);
    }

    public static int UtcOffset(int? minutes) => Math.Clamp(minutes ?? 0, -Visit.MaxUtcOffsetMinutes, Visit.MaxUtcOffsetMinutes);
}

/// <summary>The ports a visit registration needs (V1, B4, T1, T2).</summary>
internal sealed record RegistrarPorts(
    IVisitRepository Visits,
    IPatientLookup Patients,
    IPayerLookup Payers,
    IDoctorLookup Doctors,
    ICatalogueLookup Catalogue,
    IOrderRulesLookup Rules,
    IPriceLookup Prices,
    ITenantSettings Settings,
    IUserPrivileges Privileges,
    IReferenceNumbers Numbers);

/// <summary>One visit to register; <paramref name="HeaderChecked"/> skips the contract and doctor checks a batch already made.</summary>
internal sealed record VisitRegistrationInput(
    Guid PatientId,
    VisitLocationType LocationType,
    string? TreatmentNo,
    Guid? ReferringDoctorId,
    Guid PayerId,
    Guid ContractTierId,
    string? InsuranceId,
    Guid? DiagnosisId,
    Guid? BookingId,
    Guid? BatchId,
    bool Urgent,
    bool Biohazard,
    bool CollectedOutside,
    IReadOnlyList<OrderLineRequest> Lines,
    IReadOnlyList<PreparationAnswerRequest>? Answers,
    VisitDiscountRequest? Discount,
    bool ConfirmFrequencyWarnings,
    int UtcOffsetMinutes,
    Guid? CollectorUserId,
    bool HeaderChecked = false);

/// <summary>
/// V1 without the commit: every precondition of the transition table, the order plan, the numbers (accession, samples)
/// and the new <see cref="Visit"/>. Shared by walk-in registration, booking conversion and batch members, so every path
/// applies the same rules. The caller stages the visit and commits once.
/// </summary>
internal sealed class VisitRegistrar(RegistrarPorts ports)
{
    public async Task<(Visit Visit, OrderPlan Plan)> RegisterAsync(VisitRegistrationInput input, Guid actorUserId, Guid branchId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var patient = await RegistrationRules.RequirePatientAsync(ports.Patients, input.PatientId, cancellationToken);
        await RegistrationRules.CheckPatientSettingsAsync(ports.Settings, patient, cancellationToken);
        if (!input.HeaderChecked)
        {
            await RegistrationRules.RequireContractAsync(ports.Payers, input.PayerId, input.ContractTierId, cancellationToken);
            await RegistrationRules.CheckDoctorAsync(ports.Doctors, ports.Settings, input.ReferringDoctorId, branchId, cancellationToken);
        }

        await RegistrationRules.CheckDiagnosisAsync(ports.Rules, input.DiagnosisId, cancellationToken);
        if (input.Lines.Count == 0 && input.DiagnosisId is null)
        {
            throw new DomainException(RegistrationCodes.LinesRequired, "Add at least one service.", DomainFault.Validation);
        }

        var planner = new OrderPlanner(ports.Catalogue, ports.Rules, ports.Prices, ports.Visits);
        var plan = await planner.PlanAsync(
            new OrderContext(new OrderPatient(patient.Id, patient.Gender), input.PayerId, input.ContractTierId, input.DiagnosisId, input.Lines, new HashSet<Guid>(), null, nowUtc, input.UtcOffsetMinutes),
            cancellationToken);
        plan.ThrowIfRefused(input.ConfirmFrequencyWarnings);

        var answers = RegistrationRules.Answers(input.Answers, plan.Preparations);
        var discount = await RegistrationRules.DiscountAsync(ports.Rules, ports.Settings, ports.Privileges, input.Discount, cancellationToken);
        var items = plan.Items;
        var accession = await ports.Numbers.NextAsync(Visit.AccessionKind, Visit.AccessionPrefix, nowUtc, cancellationToken);
        var sampleCount = TubePlanner.CountNewTubes([], [.. Visit.TubeNeedsOf(items).Select(spec => spec?.Key)]);
        var samples = await RegistrationRules.SampleNumbersAsync(ports.Numbers, sampleCount, nowUtc, cancellationToken);

        var visit = Visit.Register(
            new VisitRegistration(
                accession,
                patient.Id,
                branchId,
                input.LocationType,
                input.TreatmentNo,
                input.ReferringDoctorId,
                input.PayerId,
                input.ContractTierId,
                input.InsuranceId,
                input.DiagnosisId,
                input.BookingId,
                input.BatchId,
                input.Urgent,
                input.Biohazard,
                input.CollectedOutside,
                input.UtcOffsetMinutes,
                input.CollectorUserId),
            items,
            answers,
            discount,
            samples,
            actorUserId,
            nowUtc);
        return (visit, plan);
    }
}

/// <summary>Maps an order plan onto the dry-run response (tubes as BR-02 would assign them on <paramref name="visit"/>).</summary>
internal static class OrderPlanViews
{
    public static OrderCheckResult ToCheckResult(OrderPlan plan, Visit? visit, string currency)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var tubes = Tubes(plan.Items, visit);
        return new OrderCheckResult(
            [.. plan.Lines.Select(line => Preview(line.Item, line))],
            tubes,
            new OrderTotals(plan.Lines.Sum(line => line.Item.ListPrice), plan.Lines.Sum(line => line.Item.PatientShare), plan.Lines.Sum(line => line.Item.PayerShare)),
            plan.Issues,
            plan.Warnings,
            [.. plan.Preparations.Select(ToItem)],
            currency);
    }

    public static PreparationQuestionItem ToItem(PreparationQuestion question) =>
        new(question.PreparationId, question.Code, question.QuestionEn, question.QuestionAr, question.AnswerType.ToString(), question.Options, question.Gender.ToString(), question.ServiceIds);

    public static string KeyText(TubeKey key) => $"{key.ClinicalGroupId:N}:{key.SampleTypeId:N}:{key.SampleConditionId?.ToString("N") ?? "-"}";

    private static string? KeyOf(VisitOrderItem item) =>
        !item.Reserved && item.Tube is not null && Visit.SampleCarryingTypes.Contains(item.ServiceType) ? KeyText(item.Tube.Key) : null;

    private static OrderLinePreview Preview(VisitOrderItem item, PlannedLine? line) =>
        new(
            item.ServiceId,
            item.ServiceCode,
            item.ServiceNameEn,
            item.ServiceNameAr,
            item.ServiceType,
            item.Kind.ToString(),
            item.Reserved ? nameof(VisitLineStatus.Reserved) : nameof(VisitLineStatus.Ordered),
            line?.Priced ?? true,
            item.ListPrice,
            item.PatientShare,
            item.PayerShare,
            item.ExpectedResultAtUtc,
            line?.Precaution,
            line?.AlertCondition,
            item.Origin.ToString(),
            item.AddedByRuleId,
            line?.RuleNameEn,
            line?.RuleNameAr,
            KeyOf(item),
            [.. item.Members.Select(member => Preview(member with { Reserved = item.Reserved }, null))]);

    private static List<TubePreview> Tubes(IReadOnlyList<VisitOrderItem> items, Visit? visit)
    {
        var existing = visit?.Tubes.Select(tube => new TubePlanner.ExistingTube(tube.Id, tube.Key, visit.Lines.Any(line => line.TubeId == tube.Id && line.Status == VisitLineStatus.Ordered))).ToList() ?? [];
        var needs = items.Where(item => !item.Reserved)
            .SelectMany(item => item.Kind == VisitLineKind.Package ? item.Members : [item])
            .Where(item => item.Tube is not null && Visit.SampleCarryingTypes.Contains(item.ServiceType))
            .ToList();
        var plan = TubePlanner.Assign(existing, [.. needs.Select(item => (TubeKey?)item.Tube!.Key)]);

        var previews = new List<TubePreview>();
        for (var index = 0; index < needs.Count; index++)
        {
            var item = needs[index];
            var spec = item.Tube!;
            var key = KeyText(spec.Key);
            var assignment = plan.Assignments[index]!;
            var existingTube = assignment.ExistingTubeId is { } tubeId ? visit!.Tubes.First(tube => tube.Id == tubeId) : null;
            var preview = previews.FirstOrDefault(candidate => candidate.Key == key && candidate.ExistingTubeId == existingTube?.Id);
            if (preview is null)
            {
                previews.Add(new TubePreview(key, existingTube?.Id, existingTube?.SampleNumber, spec.UnitCode, spec.ClinicalGroupNameEn, spec.ClinicalGroupNameAr, spec.SampleTypeNameEn, spec.SampleTypeNameAr, spec.SampleConditionNameEn, spec.SampleConditionNameAr, [item.ServiceId]));
            }
            else
            {
                previews[previews.IndexOf(preview)] = preview with { ServiceIds = [.. preview.ServiceIds, item.ServiceId] };
            }
        }

        return previews;
    }
}

/// <summary>Name lookups for registers and documents: one call per distinct id, cached for the request.</summary>
internal sealed class ContractNames(IPayerLookup payers)
{
    private readonly Dictionary<Guid, PayerSummary?> _payers = [];
    private readonly Dictionary<Guid, ContractTierSummary?> _tiers = [];

    public async Task<PayerSummary?> PayerAsync(Guid? payerId, CancellationToken cancellationToken)
    {
        if (payerId is not { } id)
        {
            return null;
        }

        if (!_payers.TryGetValue(id, out var payer))
        {
            payer = await payers.FindPayerAsync(id, cancellationToken);
            _payers[id] = payer;
        }

        return payer;
    }

    public async Task<ContractTierSummary?> TierAsync(Guid? tierId, CancellationToken cancellationToken)
    {
        if (tierId is not { } id)
        {
            return null;
        }

        if (!_tiers.TryGetValue(id, out var tier))
        {
            tier = await payers.FindTierAsync(id, cancellationToken);
            _tiers[id] = tier;
        }

        return tier;
    }

    public async Task<ContractRef> ContractAsync(Guid payerId, Guid tierId, CancellationToken cancellationToken)
    {
        var payer = await PayerAsync(payerId, cancellationToken);
        var tier = await TierAsync(tierId, cancellationToken);
        return new ContractRef(payerId, payer?.NameEn ?? string.Empty, payer?.NameAr ?? string.Empty, tierId, tier?.NameEn ?? string.Empty, tier?.NameAr ?? string.Empty);
    }
}
