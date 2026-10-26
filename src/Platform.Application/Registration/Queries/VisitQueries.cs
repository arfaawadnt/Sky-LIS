using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Pricing;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Contracts.Registration;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration.Queries;

// Registers and documents of the visit (intake §14). Queries carry a policy like commands (D-04) and never commit.

/// <summary>CP-10 filters; dates are UTC instants (the page sends its local day boundaries).</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record SearchVisitsQuery(
    string? Accession,
    string? PatientNumber,
    string? Name,
    string? Mobile,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? BranchId,
    Guid? PayerId,
    string? Status,
    Guid? PatientId,
    Guid? BatchId,
    int? Page,
    int? PageSize) : IQuery<Paged<VisitListItem>>;

public sealed class SearchVisitsValidator : AbstractValidator<SearchVisitsQuery>
{
    public SearchVisitsValidator()
    {
        RuleFor(query => query.Accession).MaximumLength(Visit.MaxAccessionLength);
        RuleFor(query => query.PatientNumber).MaximumLength(40);
        RuleFor(query => query.Name).MaximumLength(200);
        RuleFor(query => query.Mobile).MaximumLength(20);
        RuleFor(query => query.Status).IsEnumName(typeof(VisitStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class SearchVisitsHandler(
    IVisitRepository visits,
    IBatchRepository batches,
    IPatientLookup patients,
    IBranchLookup branches,
    IPayerLookup payers,
    IVisitBalanceLookup balances,
    IUserPrivileges privileges,
    ILaboratoryCountry laboratoryCountry) : IQueryHandler<SearchVisitsQuery, Paged<VisitListItem>>
{
    public async Task<Paged<VisitListItem>> Handle(SearchVisitsQuery query, CancellationToken cancellationToken)
    {
        var canSeeVip = privileges.SpecialRights.CanSeeVip;
        var mobileLength = (await laboratoryCountry.FindAsync(cancellationToken))?.MobileLength;
        var page = await visits.SearchAsync(VisitFilters.Of(query, canSeeVip, mobileLength), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var items = await new VisitRowComposer(patients, branches, payers, balances, batches).ComposeAsync(page.Items, canSeeVip, cancellationToken);
        return new Paged<VisitListItem>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

/// <summary>CP-10 export (<c>registration.export</c>): every match as rows; the page writes the CSV (≤ 10 000 rows, else REG-050).</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Export)]
public sealed record ExportVisitsQuery(
    string? Accession,
    string? PatientNumber,
    string? Name,
    string? Mobile,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? BranchId,
    Guid? PayerId,
    string? Status,
    Guid? PatientId,
    Guid? BatchId) : IQuery<IReadOnlyList<VisitListItem>>;

public sealed class ExportVisitsValidator : AbstractValidator<ExportVisitsQuery>
{
    public ExportVisitsValidator() =>
        RuleFor(query => query.Status).IsEnumName(typeof(VisitStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
}

public sealed class ExportVisitsHandler(
    IVisitRepository visits,
    IBatchRepository batches,
    IPatientLookup patients,
    IBranchLookup branches,
    IPayerLookup payers,
    IVisitBalanceLookup balances,
    IUserPrivileges privileges,
    ILaboratoryCountry laboratoryCountry) : IQueryHandler<ExportVisitsQuery, IReadOnlyList<VisitListItem>>
{
    public const int MaxRows = 10_000;

    public async Task<IReadOnlyList<VisitListItem>> Handle(ExportVisitsQuery query, CancellationToken cancellationToken)
    {
        var canSeeVip = privileges.SpecialRights.CanSeeVip;
        var filter = VisitFilters.Of(new SearchVisitsQuery(query.Accession, query.PatientNumber, query.Name, query.Mobile, query.FromUtc, query.ToUtc, query.BranchId, query.PayerId, query.Status, query.PatientId, query.BatchId, null, null), canSeeVip, (await laboratoryCountry.FindAsync(cancellationToken))?.MobileLength);
        var rows = await visits.SearchAllAsync(filter, MaxRows, cancellationToken);
        if (rows.Count > MaxRows)
        {
            throw new DomainException(RegistrationCodes.ExportTooLarge, $"The export is limited to {MaxRows} visits; narrow the filters.", DomainFault.Validation);
        }

        var composer = new VisitRowComposer(patients, branches, payers, balances, batches);
        var items = new List<VisitListItem>(rows.Count);
        foreach (var chunk in rows.Chunk(400))
        {
            items.AddRange(await composer.ComposeAsync(chunk, canSeeVip, cancellationToken));
        }

        return items;
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetVisitQuery(Guid VisitId) : IQuery<VisitDetail>;

public sealed class GetVisitHandler(
    IVisitRepository visits,
    IBatchRepository batches,
    IPatientLookup patients,
    IBranchLookup branches,
    IPayerLookup payers,
    IDoctorLookup doctors,
    IUserDirectory users,
    IVisitBalanceLookup balances) : IQueryHandler<GetVisitQuery, VisitDetail>
{
    public async Task<VisitDetail> Handle(GetVisitQuery query, CancellationToken cancellationToken)
    {
        var visit = await visits.GetReadOnlyAsync(query.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        return await new VisitDetailComposer(batches, patients, branches, payers, doctors, users, balances).ComposeAsync(visit, cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetVisitLabelsQuery(Guid VisitId) : IQuery<VisitLabelsDocument>;

/// <summary>One label per live tube (LDM §2.2 content), printed <c>labelCopies</c> times each.</summary>
public sealed class GetVisitLabelsHandler(
    IVisitRepository visits,
    IPatientLookup patients,
    IRegistrationDirectory directory,
    ITenantSettings settings) : IQueryHandler<GetVisitLabelsQuery, VisitLabelsDocument>
{
    public async Task<VisitLabelsDocument> Handle(GetVisitLabelsQuery query, CancellationToken cancellationToken)
    {
        var visit = await visits.GetReadOnlyAsync(query.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        var patient = await patients.GetAsync(visit.PatientId, cancellationToken);
        var letterhead = await directory.LetterheadAsync(visit.BranchId, cancellationToken);
        var copies = await settings.GetAsync(SettingKeys.LabelCopies, cancellationToken);

        var labels = visit.Tubes
            .Select(tube => (Tube: tube, Lines: visit.Lines.Where(line => line.TubeId == tube.Id && line.Status == VisitLineStatus.Ordered).ToList()))
            .Where(pair => pair.Lines.Count > 0)
            .Select(pair => new SampleLabel(
                pair.Tube.Id,
                pair.Tube.SampleNumber,
                pair.Tube.UnitCode,
                pair.Tube.SampleTypeNameEn,
                pair.Tube.SampleTypeNameAr,
                pair.Tube.SampleConditionNameEn,
                pair.Tube.SampleConditionNameAr,
                visit.Accession,
                patient?.Number,
                patient?.FullName,
                patient?.NameAr,
                patient?.Gender,
                patient?.Age.Years ?? 0,
                visit.RegisteredAtUtc,
                letterhead.BranchNameEn,
                letterhead.BranchNameAr,
                visit.Urgent,
                [.. pair.Lines.OrderBy(line => line.Sequence).Select(line => line.ServiceCode)],
                visit.Biohazard,
                pair.Tube.CollectedOutside))
            .ToList();

        return new VisitLabelsDocument(letterhead, visit.Accession, Math.Max(1, copies), visit.UtcOffsetMinutes, labels);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetJobOrderQuery(Guid VisitId) : IQuery<JobOrderDocument>;

/// <summary>The job order (LDM §4.5 "Sampling details"): visit, patient, balance, per service unit / expected date / sample, answers.</summary>
public sealed class GetJobOrderHandler(
    IVisitRepository visits,
    IBatchRepository batches,
    IPatientLookup patients,
    IBranchLookup branches,
    IPayerLookup payers,
    IDoctorLookup doctors,
    IUserDirectory users,
    IVisitBalanceLookup balances,
    IRegistrationDirectory directory,
    ITenantSettings settings,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetJobOrderQuery, JobOrderDocument>
{
    public async Task<JobOrderDocument> Handle(GetJobOrderQuery query, CancellationToken cancellationToken)
    {
        var visit = await visits.GetReadOnlyAsync(query.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        var detail = await new VisitDetailComposer(batches, patients, branches, payers, doctors, users, balances).ComposeAsync(visit, cancellationToken);
        var letterhead = await directory.LetterheadAsync(visit.BranchId, cancellationToken);
        var ordinal = await visits.VisitOrdinalAsync(visit.PatientId, visit.RegisteredAtUtc, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);
        var tubes = visit.Tubes.ToDictionary(tube => tube.Id);

        var lines = visit.Lines
            .Where(line => line.IsLive)
            .OrderBy(line => line.Sequence)
            .Select(line =>
            {
                var tube = line.TubeId is { } tubeId ? tubes.GetValueOrDefault(tubeId) : null;
                return new JobOrderLine(line.Id, line.ServiceCode, line.ServiceNameEn, line.ServiceNameAr, line.Kind.ToString(), line.Status.ToString(), tube?.UnitCode, tube?.SampleNumber, tube?.SampleTypeNameEn, tube?.SampleTypeNameAr, line.ExpectedResultAtUtc,
                    line.Kind == VisitLineKind.FormulaOperand ? visit.Lines.FirstOrDefault(parent => parent.Id == line.ParentLineId)?.ServiceCode : null);
            })
            .ToList();

        return new JobOrderDocument(
            letterhead,
            visit.Id,
            visit.Accession,
            visit.RegisteredAtUtc,
            detail.RegisteredByName,
            detail.Patient,
            ordinal,
            detail.Contract,
            detail.DoctorNameEn,
            detail.DoctorNameAr,
            visit.Urgent,
            visit.Biohazard,
            visit.AnyCollectedOutside,
            detail.BatchNumber,
            lines,
            [.. detail.Tubes.Where(tube => tube.IsLive)],
            detail.PreparationAnswers,
            detail.Balance,
            currency,
            visit.UtcOffsetMinutes,
            user.DisplayName,
            clock.UtcNow);
    }
}

/// <summary>The preparation questions for services and a gender (CP-06 "answer preparations", CP-12).</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetPreparationsQuery(IReadOnlyList<Guid> ServiceIds, string? Gender) : IQuery<IReadOnlyList<PreparationQuestionItem>>;

public sealed class GetPreparationsValidator : AbstractValidator<GetPreparationsQuery>
{
    public GetPreparationsValidator()
    {
        RuleFor(query => query.ServiceIds).NotNull().Must(ids => ids is { Count: <= Visit.MaxLines });
        RuleFor(query => query.Gender).Must(gender => gender is null or "Male" or "Female").WithMessage("The gender is Male or Female.");
    }
}

public sealed class GetPreparationsHandler(IOrderRulesLookup rules) : IQueryHandler<GetPreparationsQuery, IReadOnlyList<PreparationQuestionItem>>
{
    public async Task<IReadOnlyList<PreparationQuestionItem>> Handle(GetPreparationsQuery query, CancellationToken cancellationToken)
    {
        var ids = query.ServiceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var gender = query.Gender switch { "Male" => PreparationGender.Male, "Female" => (PreparationGender?)PreparationGender.Female, _ => null };
        return [.. (await rules.PreparationsForAsync(ids, gender, cancellationToken)).Select(OrderPlanViews.ToItem)];
    }
}

/// <summary>The dry run of CP-06 (nothing saved): what Register (or Add services on <paramref name="VisitId"/>) would order, refuse and warn.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Create)]
public sealed record CheckOrderQuery(Guid? VisitId, Guid? PatientId, Guid PayerId, Guid ContractTierId, Guid? DiagnosisId, IReadOnlyList<OrderLineRequest> Lines, int? UtcOffsetMinutes) : IQuery<OrderCheckResult>;

public sealed class CheckOrderValidator : AbstractValidator<CheckOrderQuery>
{
    public CheckOrderValidator()
    {
        RuleFor(query => query.PayerId).NotEmpty();
        RuleFor(query => query.ContractTierId).NotEmpty();
        RuleFor(query => query.Lines).NotNull().Must(lines => lines is { Count: <= Visit.MaxLines });
        RuleForEach(query => query.Lines).ChildRules(line => line.RuleFor(value => value.ServiceId).NotEmpty());
        RuleFor(query => query.UtcOffsetMinutes).InclusiveBetween(-Visit.MaxUtcOffsetMinutes, Visit.MaxUtcOffsetMinutes).When(query => query.UtcOffsetMinutes is not null);
    }
}

public sealed class CheckOrderHandler(
    IVisitRepository visits,
    IPatientLookup patients,
    IPayerLookup payers,
    ICatalogueLookup catalogue,
    IOrderRulesLookup rules,
    IPriceLookup prices,
    ITenantSettings settings,
    IClock clock,
    IFormulaOperandLookup formulas) : IQueryHandler<CheckOrderQuery, OrderCheckResult>
{
    public async Task<OrderCheckResult> Handle(CheckOrderQuery query, CancellationToken cancellationToken)
    {
        Visit? visit = null;
        if (query.VisitId is { } visitId)
        {
            visit = await visits.GetReadOnlyAsync(visitId, cancellationToken)
                ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        }

        var patientId = visit?.PatientId ?? query.PatientId;
        PatientSummary? patient = null;
        if (patientId is { } id)
        {
            patient = await patients.GetAsync(id, cancellationToken)
                ?? throw new DomainException(RegistrationCodes.PatientNotFound, "The patient file does not exist.", DomainFault.Validation);
        }

        var payerId = visit?.PayerId ?? query.PayerId;
        var tierId = visit?.ContractTierId ?? query.ContractTierId;
        await RegistrationRules.RequireContractAsync(payers, payerId, tierId, cancellationToken);
        await RegistrationRules.CheckDiagnosisAsync(rules, visit is null ? query.DiagnosisId : null, cancellationToken);

        var planner = new OrderPlanner(catalogue, rules, prices, visits, formulas);
        var plan = await planner.PlanAsync(
            new OrderContext(
                patient is null ? null : new OrderPatient(patient.Id, patient.Gender),
                payerId,
                tierId,
                visit is null ? query.DiagnosisId : null,
                query.Lines,
                visit?.LiveServiceIds ?? new HashSet<Guid>(),
                visit?.Id,
                clock.UtcNow,
                visit?.UtcOffsetMinutes ?? RegistrationRules.UtcOffset(query.UtcOffsetMinutes),
                ExistingTests: visit?.LiveTestHolders()),
            cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);
        return OrderPlanViews.ToCheckResult(plan, visit, currency);
    }
}

/// <summary>CP-06 service entry: shortcut number, code, short name or alias, then shortcut set; otherwise candidates for the picker.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Create)]
public sealed record ResolveServiceEntryQuery(string Text) : IQuery<ServiceEntryResult>;

public sealed class ResolveServiceEntryValidator : AbstractValidator<ResolveServiceEntryQuery>
{
    public ResolveServiceEntryValidator() => RuleFor(query => query.Text).NotEmpty().MaximumLength(100);
}

public sealed class ResolveServiceEntryHandler(IServiceEntryFinder finder, IOrderRulesLookup rules) : IQueryHandler<ResolveServiceEntryQuery, ServiceEntryResult>
{
    public const int CandidateLimit = 20;

    public async Task<ServiceEntryResult> Handle(ResolveServiceEntryQuery query, CancellationToken cancellationToken)
    {
        var text = query.Text.Trim();
        var exact = await finder.FindExactAsync(text, cancellationToken);
        if (exact.Count == 1)
        {
            return new ServiceEntryResult("Service", exact, null, null, null, []);
        }

        if (exact.Count == 0 && await rules.ShortcutSetByCodeAsync(text, cancellationToken) is { } set)
        {
            var services = (await finder.GetManyAsync(set.ServiceIds, cancellationToken)).ToDictionary(service => service.Id);
            var ordered = set.ServiceIds.Where(services.ContainsKey).Select(id => services[id]).ToList();
            return new ServiceEntryResult("ShortcutSet", ordered, set.Code, set.NameEn, set.NameAr, []);
        }

        var candidates = exact.Count > 1 ? exact : await finder.SearchAsync(text, CandidateLimit, cancellationToken);
        return new ServiceEntryResult("None", [], null, null, null, candidates);
    }
}

/// <summary>What the registration page needs from the tenant parameters and the actor's special rights.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record GetRegistrationSettingsQuery : IQuery<RegistrationPageSettings>;

/// <summary>
/// The parameters and rights of the registration page, and the names of the session's branch (CP-04) for the page header
/// (validation 2026-10-11: the new-registration header showed no branch).
/// </summary>
public sealed class GetRegistrationSettingsHandler(ITenantSettings settings, IUserPrivileges privileges, ICurrentUser user, IBranchLookup branches) : IQueryHandler<GetRegistrationSettingsQuery, RegistrationPageSettings>
{
    public async Task<RegistrationPageSettings> Handle(GetRegistrationSettingsQuery query, CancellationToken cancellationToken)
    {
        var rights = privileges.SpecialRights;
        var branch = user.BranchId is { } branchId ? await branches.FindAsync(branchId, cancellationToken) : null;
        return new RegistrationPageSettings(
            await settings.GetAsync(SettingKeys.AutoPrintLabels, cancellationToken),
            await settings.GetAsync(SettingKeys.AutoPrintJobOrder, cancellationToken),
            await settings.GetAsync(SettingKeys.AutoPrintReceipt, cancellationToken),
            Math.Max(1, await settings.GetAsync(SettingKeys.LabelCopies, cancellationToken)),
            await settings.GetAsync(SettingKeys.MandatoryMobile, cancellationToken),
            await settings.GetAsync(SettingKeys.MandatoryDoctor, cancellationToken),
            await settings.GetAsync(SettingKeys.MandatoryNationalId, cancellationToken),
            rights.EffectiveDiscountCap(await settings.GetAsync(SettingKeys.DiscountCapPercent, cancellationToken)),
            rights.CanCancel,
            rights.CanTransfer,
            rights.CanPay,
            rights.CanSeeVip,
            await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken),
            branch?.NameEn,
            branch?.NameAr);
    }
}

/// <summary>CP-12 price query (nothing saved): prices from the contract (BR-06/07), expected dates (BR-13), precautions, alerts, preparations.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record PriceQuoteQuery(Guid? PayerId, Guid? ContractTierId, IReadOnlyList<Guid> ServiceIds, string? Gender, int? UtcOffsetMinutes) : IQuery<PriceQuoteDocument>;

public sealed class PriceQuoteValidator : AbstractValidator<PriceQuoteQuery>
{
    public PriceQuoteValidator()
    {
        RuleFor(query => query.ServiceIds).NotNull().Must(ids => ids is { Count: >= 1 and <= Visit.MaxLines }).WithMessage($"Quote 1 to {Visit.MaxLines} services.");
        RuleForEach(query => query.ServiceIds).NotEmpty();
        RuleFor(query => query.Gender).Must(gender => gender is null or "Male" or "Female").WithMessage("The gender is Male or Female.");
        RuleFor(query => query).Must(query => query.ContractTierId is null || query.PayerId is not null).WithName("payerId").WithMessage("A contract tier needs its payer.");
        RuleFor(query => query.UtcOffsetMinutes).InclusiveBetween(-Visit.MaxUtcOffsetMinutes, Visit.MaxUtcOffsetMinutes).When(query => query.UtcOffsetMinutes is not null);
    }
}

public sealed class PriceQuoteHandler(
    IVisitRepository visits,
    IPayerLookup payers,
    ICatalogueLookup catalogue,
    IOrderRulesLookup rules,
    IPriceLookup prices,
    IRegistrationDirectory directory,
    ITenantSettings settings,
    ICurrentUser user,
    IClock clock,
    IFormulaOperandLookup formulas) : IQueryHandler<PriceQuoteQuery, PriceQuoteDocument>
{
    public async Task<PriceQuoteDocument> Handle(PriceQuoteQuery query, CancellationToken cancellationToken)
    {
        var (payerId, tierId) = (query.PayerId, query.ContractTierId);
        if (payerId is null && await payers.FindDefaultAsync(cancellationToken) is { } cash)
        {
            (payerId, tierId) = (cash.PayerId, cash.ContractTierId);
        }

        if (payerId is { } payer && tierId is { } tier)
        {
            await RegistrationRules.RequireContractAsync(payers, payer, tier, cancellationToken);
        }

        var planner = new OrderPlanner(catalogue, rules, prices, visits, formulas);
        var offset = RegistrationRules.UtcOffset(query.UtcOffsetMinutes);
        var plan = await planner.PlanAsync(
            new OrderContext(
                query.Gender is null ? null : new OrderPatient(Guid.Empty, query.Gender),
                payerId,
                tierId,
                null,
                [.. query.ServiceIds.Select(id => new OrderLineRequest(id, false))],
                new HashSet<Guid>(),
                null,
                clock.UtcNow,
                offset,
                ForQuote: true),
            cancellationToken);

        var lines = plan.Lines.Select(line => new PriceQuoteDocumentLine(
            line.Item.ServiceId,
            line.Item.ServiceCode,
            line.Item.ServiceNameEn,
            line.Item.ServiceNameAr,
            line.Item.ServiceType,
            line.Priced,
            line.Item.ListPrice,
            line.Item.PatientShare,
            line.Item.PayerShare,
            line.Item.ExpectedResultAtUtc,
            line.Precaution,
            line.AlertCondition,
            line.Item.Kind.ToString(),
            line.FormulaServiceCode)).ToList();
        var contract = payerId is { } p && tierId is { } t ? await new ContractNames(payers).ContractAsync(p, t, cancellationToken) : null;
        var branch = user.BranchId ?? Guid.Empty;

        return new PriceQuoteDocument(
            await directory.LetterheadAsync(branch, cancellationToken),
            contract,
            await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken),
            lines,
            lines.Sum(line => line.ListPrice),
            lines.Sum(line => line.PatientShare),
            lines.Sum(line => line.PayerShare),
            [.. plan.Preparations.Select(OrderPlanViews.ToItem)],
            plan.Issues,
            offset,
            user.DisplayName,
            clock.UtcNow);
    }
}

/// <summary>V7 preview (CP-25): the visit re-priced under another contract, with the resulting balance; nothing saved.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Approve)]
public sealed record PreviewContractTransferQuery(Guid VisitId, Guid PayerId, Guid ContractTierId) : IQuery<TransferPreview>;

public sealed class PreviewContractTransferValidator : AbstractValidator<PreviewContractTransferQuery>
{
    public PreviewContractTransferValidator()
    {
        RuleFor(query => query.VisitId).NotEmpty();
        RuleFor(query => query.PayerId).NotEmpty();
        RuleFor(query => query.ContractTierId).NotEmpty();
    }
}

public sealed class PreviewContractTransferHandler(
    IVisitRepository visits,
    IPayerLookup payers,
    IPriceLookup prices,
    IVisitBalanceLookup balances) : IQueryHandler<PreviewContractTransferQuery, TransferPreview>
{
    public async Task<TransferPreview> Handle(PreviewContractTransferQuery query, CancellationToken cancellationToken)
    {
        var visit = await visits.GetReadOnlyAsync(query.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        await RegistrationRules.RequireContractAsync(payers, query.PayerId, query.ContractTierId, cancellationToken);

        var priced = await Commands.TransferRights.PricesAsync(prices, visit, query.PayerId, query.ContractTierId, cancellationToken);
        var outcome = visit.PreviewTransfer(priced);
        var names = new ContractNames(payers);
        var balance = await balances.GetAsync(visit.Id, cancellationToken);
        var (paid, refunded) = (balance?.Paid ?? 0m, balance?.Refunded ?? 0m);
        var before = visit.Lines.ToDictionary(line => line.Id);

        var lines = outcome.Lines.Select(after =>
        {
            var line = before[after.LineId];
            return new TransferPreviewLine(
                line.Id, line.ServiceId, line.ServiceCode, line.ServiceNameEn, line.ServiceNameAr, after.Priced,
                line.ListPrice, line.PatientShare, line.PayerShare, line.Discount,
                after.ListPrice, after.PatientShare, after.PayerShare, after.Discount);
        }).ToList();

        return new TransferPreview(
            visit.Id,
            visit.Accession,
            await names.ContractAsync(visit.PayerId, visit.ContractTierId, cancellationToken),
            await names.ContractAsync(query.PayerId, query.ContractTierId, cancellationToken),
            lines,
            lines.All(line => line.Priced),
            visit.PatientShareTotal,
            outcome.PatientShareTotal,
            visit.PayerShareTotal,
            outcome.PayerShareTotal,
            paid,
            refunded,
            visit.PatientShareTotal - paid + refunded,
            outcome.PatientShareTotal - paid + refunded);
    }
}

/// <summary>CP-26 cancelled list.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record ListCancelledLinesQuery(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId, int? Page, int? PageSize) : IQuery<Paged<CancelledLineItem>>;

public sealed class ListCancelledLinesValidator : AbstractValidator<ListCancelledLinesQuery>
{
    public ListCancelledLinesValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListCancelledLinesHandler(IVisitRepository visits, IPatientLookup patients, IUserDirectory users, IUserPrivileges privileges) : IQueryHandler<ListCancelledLinesQuery, Paged<CancelledLineItem>>
{
    public async Task<Paged<CancelledLineItem>> Handle(ListCancelledLinesQuery query, CancellationToken cancellationToken)
    {
        var page = await visits.CancelledLinesAsync(query.FromUtc, query.ToUtc, query.BranchId, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var items = await CancelledLines.ComposeAsync(page.Items, patients, users, privileges.SpecialRights.CanSeeVip, cancellationToken);
        return new Paged<CancelledLineItem>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Export)]
public sealed record ExportCancelledLinesQuery(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId) : IQuery<IReadOnlyList<CancelledLineItem>>;

public sealed class ExportCancelledLinesHandler(IVisitRepository visits, IPatientLookup patients, IUserDirectory users, IUserPrivileges privileges) : IQueryHandler<ExportCancelledLinesQuery, IReadOnlyList<CancelledLineItem>>
{
    public async Task<IReadOnlyList<CancelledLineItem>> Handle(ExportCancelledLinesQuery query, CancellationToken cancellationToken)
    {
        var rows = await visits.CancelledLinesAllAsync(query.FromUtc, query.ToUtc, query.BranchId, ExportVisitsHandler.MaxRows, cancellationToken);
        if (rows.Count > ExportVisitsHandler.MaxRows)
        {
            throw new DomainException(RegistrationCodes.ExportTooLarge, $"The export is limited to {ExportVisitsHandler.MaxRows} rows; narrow the filters.", DomainFault.Validation);
        }

        var items = new List<CancelledLineItem>(rows.Count);
        foreach (var chunk in rows.Chunk(400))
        {
            items.AddRange(await CancelledLines.ComposeAsync(chunk, patients, users, privileges.SpecialRights.CanSeeVip, cancellationToken));
        }

        return items;
    }
}

/// <summary>CP-25 transfer log.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record ListTransfersQuery(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, string? Kind, Guid? VisitId, int? Page, int? PageSize) : IQuery<Paged<TransferLogItem>>;

public sealed class ListTransfersValidator : AbstractValidator<ListTransfersQuery>
{
    public ListTransfersValidator()
    {
        RuleFor(query => query.Kind).IsEnumName(typeof(VisitTransferKind), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Kind));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListTransfersHandler(IVisitRepository visits, IPayerLookup payers, IUserDirectory users) : IQueryHandler<ListTransfersQuery, Paged<TransferLogItem>>
{
    public async Task<Paged<TransferLogItem>> Handle(ListTransfersQuery query, CancellationToken cancellationToken)
    {
        VisitTransferKind? kind = string.IsNullOrEmpty(query.Kind) ? null : Enum.Parse<VisitTransferKind>(query.Kind, ignoreCase: true);
        var page = await visits.TransfersAsync(query.FromUtc, query.ToUtc, kind, query.VisitId, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var names = new ContractNames(payers);
        var actors = await users.GetNamesAsync([.. page.Items.Select(row => row.ActorUserId).Distinct()], cancellationToken);
        var items = new List<TransferLogItem>(page.Items.Count);
        foreach (var row in page.Items)
        {
            var fromPayer = await names.PayerAsync(row.FromPayerId, cancellationToken);
            var fromTier = await names.TierAsync(row.FromContractTierId, cancellationToken);
            var toPayer = await names.PayerAsync(row.ToPayerId, cancellationToken);
            var toTier = await names.TierAsync(row.ToContractTierId, cancellationToken);
            items.Add(new TransferLogItem(
                row.Id, row.VisitId, row.Accession, row.Kind.ToString(), row.ServiceCode,
                fromPayer?.NameEn, fromPayer?.NameAr, fromTier?.NameEn, fromTier?.NameAr,
                toPayer?.NameEn, toPayer?.NameAr, toTier?.NameEn, toTier?.NameAr,
                row.PatientShareBefore, row.PatientShareAfter, row.PayerShareBefore, row.PayerShareAfter,
                row.Reason, actors.GetValueOrDefault(row.ActorUserId)?.DisplayName, row.AtUtc));
        }

        return new Paged<TransferLogItem>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

/// <summary>Sending laboratories for CP-09 (payer, tier, courier, phone, doctor) under <c>registration.view</c>.</summary>
[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.View)]
public sealed record ListSendingLabsQuery : IQuery<IReadOnlyList<SendingLabOption>>;

public sealed class ListSendingLabsHandler(ISendingLabLookup sendingLabs) : IQueryHandler<ListSendingLabsQuery, IReadOnlyList<SendingLabOption>>
{
    public Task<IReadOnlyList<SendingLabOption>> Handle(ListSendingLabsQuery query, CancellationToken cancellationToken) => sendingLabs.ListActiveAsync(cancellationToken);
}

/// <summary>The register filter from the query (status name parsed; VIP rule of intake Q12; a mobile typed with the trunk 0 also finds a file stored without it, F-08).</summary>
internal static class VisitFilters
{
    public static VisitSearchFilter Of(SearchVisitsQuery query, bool includeVip, int? mobileLength = null) =>
        new(
            Blank(query.Accession),
            Blank(query.PatientNumber),
            Blank(query.Name),
            Blank(query.Mobile) is { } mobile ? Platform.Domain.Patients.Patient.MobileSearchDigits(new string(Platform.Domain.Patients.Patient.FoldDigits(mobile).Where(char.IsAsciiDigit).ToArray()), mobileLength) : null,
            query.FromUtc,
            query.ToUtc,
            query.BranchId,
            query.PayerId,
            string.IsNullOrEmpty(query.Status) ? null : Enum.Parse<VisitStatus>(query.Status, ignoreCase: true),
            query.PatientId,
            query.BatchId,
            includeVip);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Joins names (patients, branches, contracts, batches) and balances onto register rows; VIP files are masked without the right.</summary>
internal sealed class VisitRowComposer(IPatientLookup patients, IBranchLookup branches, IPayerLookup payers, IVisitBalanceLookup balances, IBatchRepository batches)
{
    private readonly Dictionary<Guid, BranchSummary?> _branches = [];

    public async Task<IReadOnlyList<VisitListItem>> ComposeAsync(IReadOnlyList<VisitRow> rows, bool canSeeVip, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var people = await patients.GetManyAsync([.. rows.Select(row => row.PatientId).Distinct()], cancellationToken);
        var money = await balances.GetManyAsync([.. rows.Select(row => row.Id)], cancellationToken);
        var batchIds = rows.Where(row => row.BatchId.HasValue).Select(row => row.BatchId!.Value).Distinct().ToList();
        var batchNumbers = new Dictionary<Guid, string>();
        foreach (var batchId in batchIds)
        {
            if (await batches.GetReadOnlyAsync(batchId, cancellationToken) is { } batch)
            {
                batchNumbers[batchId] = batch.Number;
            }
        }

        var names = new ContractNames(payers);
        var items = new List<VisitListItem>(rows.Count);
        foreach (var row in rows)
        {
            var person = people.GetValueOrDefault(row.PatientId);
            var masked = person is { IsVip: true } && !canSeeVip;
            var branch = await BranchAsync(row.BranchId, cancellationToken);
            var payer = await names.PayerAsync(row.PayerId, cancellationToken);
            var tier = await names.TierAsync(row.ContractTierId, cancellationToken);
            var balance = money.GetValueOrDefault(row.Id);
            items.Add(new VisitListItem(
                row.Id,
                row.Accession,
                row.RegisteredAtUtc,
                row.BranchId,
                branch?.NameEn,
                branch?.NameAr,
                row.PatientId,
                person?.Number,
                masked ? null : person?.FullName,
                masked ? null : person?.NameAr,
                masked ? null : person?.Mobile,
                masked,
                row.PayerId,
                payer?.NameEn,
                payer?.NameAr,
                tier?.NameEn,
                tier?.NameAr,
                row.Status.ToString(),
                row.LocationType.ToString(),
                row.Urgent,
                row.BatchId is { } id ? batchNumbers.GetValueOrDefault(id) : null,
                row.TotalListPrice,
                row.DiscountTotal,
                row.PatientShareTotal,
                row.PayerShareTotal,
                balance?.Paid,
                balance?.Remaining,
                row.Lines));
        }

        return items;
    }

    private async Task<BranchSummary?> BranchAsync(Guid branchId, CancellationToken cancellationToken)
    {
        if (!_branches.TryGetValue(branchId, out var branch))
        {
            branch = await branches.FindAsync(branchId, cancellationToken);
            _branches[branchId] = branch;
        }

        return branch;
    }
}

/// <summary>The visit detail (edit page, documents): names, users, contract, doctor, batch, balance.</summary>
internal sealed class VisitDetailComposer(
    IBatchRepository batches,
    IPatientLookup patients,
    IBranchLookup branches,
    IPayerLookup payers,
    IDoctorLookup doctors,
    IUserDirectory users,
    IVisitBalanceLookup balances)
{
    public async Task<VisitDetail> ComposeAsync(Visit visit, CancellationToken cancellationToken)
    {
        var patient = await patients.GetAsync(visit.PatientId, cancellationToken);
        var branch = await branches.FindAsync(visit.BranchId, cancellationToken);
        var names = new ContractNames(payers);
        var contract = await names.ContractAsync(visit.PayerId, visit.ContractTierId, cancellationToken);
        var doctor = visit.ReferringDoctorId is { } doctorId ? await doctors.GetAsync(doctorId, cancellationToken) : null;
        var batch = visit.BatchId is { } batchId ? await batches.GetReadOnlyAsync(batchId, cancellationToken) : null;
        var balance = await balances.GetAsync(visit.Id, cancellationToken);

        var userIds = visit.Lines.SelectMany(line => new[] { (Guid?)line.OrderedByUserId, line.CancelledByUserId, line.CollectorUserId })
            .Concat(visit.Transfers.Select(transfer => (Guid?)transfer.ActorUserId))
            .Append(visit.RegisteredByUserId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var people = await users.GetNamesAsync(userIds, cancellationToken);
        string? NameOf(Guid? id) => id is { } value ? people.GetValueOrDefault(value)?.DisplayName : null;

        var tubes = visit.Tubes.ToDictionary(tube => tube.Id);
        var lines = visit.Lines.OrderBy(line => line.Sequence).Select(line => new VisitLineItem(
            line.Id,
            line.ServiceId,
            line.ServiceCode,
            line.ServiceNameEn,
            line.ServiceNameAr,
            line.ServiceType,
            line.Kind.ToString(),
            line.ParentLineId,
            line.Status.ToString(),
            line.ListPrice,
            line.PatientShare,
            line.PayerShare,
            line.Discount,
            line.ExpectedResultAtUtc,
            line.TubeId,
            line.TubeId is { } tubeId ? tubes.GetValueOrDefault(tubeId)?.SampleNumber : null,
            line.CollectorUserId,
            NameOf(line.CollectorUserId),
            line.Origin.ToString(),
            line.OrderedAtUtc,
            NameOf(line.OrderedByUserId),
            line.ReleasedAtUtc,
            line.CancellationReason,
            NameOf(line.CancelledByUserId),
            line.CancelledAtUtc,
            line.VisibleOnReceipt)).ToList();

        var tubeItems = visit.Tubes.OrderBy(tube => tube.CreatedAtUtc).ThenBy(tube => tube.SampleNumber, StringComparer.Ordinal).Select(tube =>
        {
            var tubeLines = visit.Lines.Where(line => line.TubeId == tube.Id).ToList();
            return new VisitTubeItem(
                tube.Id,
                tube.SampleNumber,
                tube.UnitCode,
                tube.ClinicalGroupId,
                tube.ClinicalGroupNameEn,
                tube.ClinicalGroupNameAr,
                tube.SampleTypeId,
                tube.SampleTypeNameEn,
                tube.SampleTypeNameAr,
                tube.SampleConditionNameEn,
                tube.SampleConditionNameAr,
                tubeLines.Any(line => line.Status == VisitLineStatus.Ordered),
                [.. tubeLines.Where(line => line.Status == VisitLineStatus.Ordered).Select(line => line.ServiceCode)],
                tube.CollectedOutside,
                tube.CollectedOutsideAtUtc);
        }).ToList();

        var transfers = new List<VisitTransferItem>();
        foreach (var transfer in visit.Transfers.OrderBy(transfer => transfer.AtUtc))
        {
            var fromPayer = await names.PayerAsync(transfer.FromPayerId, cancellationToken);
            var fromTier = await names.TierAsync(transfer.FromContractTierId, cancellationToken);
            var toPayer = await names.PayerAsync(transfer.ToPayerId, cancellationToken);
            var toTier = await names.TierAsync(transfer.ToContractTierId, cancellationToken);
            transfers.Add(new VisitTransferItem(
                transfer.Id,
                transfer.Kind.ToString(),
                transfer.LineId,
                transfer.LineId is { } lineId ? visit.Lines.FirstOrDefault(line => line.Id == lineId)?.ServiceCode : null,
                fromPayer?.NameEn,
                fromPayer?.NameAr,
                fromTier?.NameEn,
                fromTier?.NameAr,
                toPayer?.NameEn,
                toPayer?.NameAr,
                toTier?.NameEn,
                toTier?.NameAr,
                transfer.PatientShareBefore,
                transfer.PatientShareAfter,
                transfer.PayerShareBefore,
                transfer.PayerShareAfter,
                transfer.Reason,
                NameOf(transfer.ActorUserId),
                transfer.AtUtc));
        }

        return new VisitDetail(
            visit.Id,
            visit.Accession,
            visit.Status.ToString(),
            visit.LocationType.ToString(),
            visit.BranchId,
            branch?.NameEn,
            branch?.NameAr,
            PatientView(visit.PatientId, patient),
            visit.OriginalPatientId,
            contract,
            visit.ReferringDoctorId,
            doctor?.NameEn,
            doctor?.NameAr,
            visit.TreatmentNo,
            visit.InsuranceId,
            visit.DiagnosisId,
            visit.BookingId,
            visit.BatchId,
            batch?.Number,
            visit.Urgent,
            visit.Biohazard,
            visit.AnyCollectedOutside,
            visit.RegisteredAtUtc,
            NameOf(visit.RegisteredByUserId),
            visit.CompletedAtUtc,
            visit.TotalListPrice,
            visit.DiscountTotal,
            visit.PatientShareTotal,
            visit.PayerShareTotal,
            visit.DiscountKind?.ToString(),
            visit.DiscountValue,
            visit.DiscountReasonId,
            visit.DiscountReason,
            visit.UtcOffsetMinutes,
            lines,
            tubeItems,
            [.. visit.PreparationAnswers.Select(answer => new PreparationAnswerItem(answer.PreparationId, answer.QuestionEn, answer.QuestionAr, answer.Answer, answer.AnsweredAtUtc))],
            transfers,
            balance is null ? null : new VisitBalanceView(balance.PatientShareTotal, balance.Paid, balance.Refunded, balance.Remaining, balance.Refundable),
            visit.RowVersion);
    }

    public static VisitPatientView PatientView(Guid patientId, PatientSummary? patient) =>
        patient is null
            ? new VisitPatientView(patientId, null, null, null, null, null, 0, 0, 0, null, null, false, null, false)
            : new VisitPatientView(patient.Id, patient.Number, patient.Title, patient.FullName, patient.NameAr, patient.Gender, patient.Age.Years, patient.Age.Months, patient.Age.Days, patient.Mobile, patient.NationalId, patient.IsVip, patient.Status, false);
}

internal static class CancelledLines
{
    public static async Task<IReadOnlyList<CancelledLineItem>> ComposeAsync(IReadOnlyList<CancelledLineRow> rows, IPatientLookup patients, IUserDirectory users, bool canSeeVip, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var people = await patients.GetManyAsync([.. rows.Select(row => row.PatientId).Distinct()], cancellationToken);
        var actors = await users.GetNamesAsync([.. rows.Where(row => row.CancelledByUserId.HasValue).Select(row => row.CancelledByUserId!.Value).Distinct()], cancellationToken);
        return [.. rows.Select(row =>
        {
            var person = people.GetValueOrDefault(row.PatientId);
            var masked = person is { IsVip: true } && !canSeeVip;
            return new CancelledLineItem(
                row.LineId, row.VisitId, row.Accession, row.PatientId, person?.Number, masked ? null : person?.FullName, row.BranchId,
                row.Code, row.NameEn, row.NameAr, row.ListPrice, row.PatientShare, row.PayerShare, row.Reason,
                row.CancelledByUserId is { } id ? actors.GetValueOrDefault(id)?.DisplayName : null, row.CancelledAtUtc);
        })];
    }
}
