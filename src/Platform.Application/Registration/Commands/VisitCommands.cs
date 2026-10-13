using System.Linq.Expressions;
using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Patients;
using Platform.Application.Pricing;
using Platform.Contracts.Registration;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration.Commands;

// Visit transitions V1–V6 (intake §7). Validators check shape; the planner and the aggregate decide (pitfall P-10).

/// <summary>Shape rules shared by every request that orders services.</summary>
internal static class OrderShape
{
    public static void Lines<T>(AbstractValidator<T> validator, Expression<Func<T, IReadOnlyList<OrderLineRequest>?>> lines)
    {
        validator.RuleFor(lines).NotNull().Must(value => value is { Count: >= 1 and <= Visit.MaxLines }).WithMessage($"Order 1 to {Visit.MaxLines} services.");
        validator.RuleForEach(Each(lines)).ChildRules(line => line.RuleFor(value => value.ServiceId).NotEmpty());
    }

    public static void Answers<T>(AbstractValidator<T> validator, Expression<Func<T, IReadOnlyList<PreparationAnswerRequest>?>> answers)
    {
        validator.RuleFor(answers).Must(value => value is null || value.Count <= 100).WithMessage("At most 100 preparation answers.");
        validator.RuleForEach(Each(answers)).ChildRules(answer =>
        {
            answer.RuleFor(value => value.PreparationId).NotEmpty();
            answer.RuleFor(value => value.Answer).MaximumLength(VisitPreparationAnswer.MaxAnswerLength);
        });
    }

    public static void Discount<T>(AbstractValidator<T> validator, Expression<Func<T, VisitDiscountRequest?>> discount)
    {
        var read = discount.Compile();
        validator.RuleFor(discount).ChildRules(value =>
        {
            value.RuleFor(item => item!.Kind).NotEmpty().IsEnumName(typeof(DiscountKind), caseSensitive: false);
            value.RuleFor(item => item!.Value).GreaterThan(0).LessThanOrEqualTo(99_999_999m);
            value.RuleFor(item => item!.ReasonId).NotEmpty();
        }).When(command => read(command) is not null);
    }

    /// <summary>The same member as a sequence (expression trees are invariant), so the rule keeps the member name.</summary>
    private static Expression<Func<T, IEnumerable<TElement>>> Each<T, TElement>(Expression<Func<T, IReadOnlyList<TElement>?>> list) =>
        Expression.Lambda<Func<T, IEnumerable<TElement>>>(Expression.Convert(list.Body, typeof(IEnumerable<TElement>)), list.Parameters);

    public static void Offset<T>(AbstractValidator<T> validator, Expression<Func<T, int?>> offset)
    {
        var read = offset.Compile();
        validator.RuleFor(offset).InclusiveBetween(-Visit.MaxUtcOffsetMinutes, Visit.MaxUtcOffsetMinutes).When(command => read(command) is not null);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Create)]
public sealed record RegisterVisitCommand(
    Guid PatientId,
    string? TreatmentNo,
    Guid? ReferringDoctorId,
    Guid PayerId,
    Guid ContractTierId,
    string? InsuranceId,
    Guid? DiagnosisId,
    Guid? BookingId,
    bool Urgent,
    bool Biohazard,
    bool CollectedOutside,
    IReadOnlyList<OrderLineRequest> Lines,
    IReadOnlyList<PreparationAnswerRequest>? PreparationAnswers,
    VisitDiscountRequest? Discount,
    bool ConfirmFrequencyWarnings,
    int? UtcOffsetMinutes) : ICommand<VisitRegisteredResponse>;

public sealed class RegisterVisitValidator : AbstractValidator<RegisterVisitCommand>
{
    public RegisterVisitValidator()
    {
        RuleFor(command => command.PatientId).NotEmpty();
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.ContractTierId).NotEmpty();
        RuleFor(command => command.TreatmentNo).MaximumLength(Visit.MaxTreatmentNoLength);
        RuleFor(command => command.InsuranceId).MaximumLength(Visit.MaxInsuranceIdLength);
        RuleFor(command => command.Lines).NotNull().Must(lines => lines is { Count: <= Visit.MaxLines }).WithMessage($"Order at most {Visit.MaxLines} services.");
        RuleForEach(command => command.Lines).ChildRules(line => line.RuleFor(value => value.ServiceId).NotEmpty());
        RuleFor(command => command).Must(command => command.Lines is { Count: > 0 } || command.DiagnosisId is not null).WithName("lines").WithMessage("Order at least one service or choose a diagnosis.");
        OrderShape.Answers(this, command => command.PreparationAnswers);
        OrderShape.Discount(this, command => command.Discount);
        OrderShape.Offset(this, command => command.UtcOffsetMinutes);
    }
}

/// <summary>V1 (and B4 when a booking id is given): registers the visit, converts the booking in the same unit of work, commits once.</summary>
public sealed class RegisterVisitHandler(
    IVisitRepository visits,
    IBookingRepository bookings,
    IPatientLookup patients,
    IPayerLookup payers,
    IDoctorLookup doctors,
    ICatalogueLookup catalogue,
    IOrderRulesLookup rules,
    IPriceLookup prices,
    ITenantSettings settings,
    IUserPrivileges privileges,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<RegisterVisitCommand, VisitRegisteredResponse>
{
    public async Task<VisitRegisteredResponse> Handle(RegisterVisitCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        var branch = BranchScope.Require(user);
        var now = clock.UtcNow;

        Booking? booking = null;
        if (command.BookingId is { } bookingId)
        {
            booking = await bookings.GetForUpdateAsync(bookingId, cancellationToken)
                ?? throw new DomainException(RegistrationCodes.BookingNotFound, "Booking not found.", DomainFault.NotFound);
            booking.EnsureConvertibleFor(command.PatientId);
        }

        var registrar = new VisitRegistrar(new RegistrarPorts(visits, patients, payers, doctors, catalogue, rules, prices, settings, privileges, numbers));
        var (visit, plan) = await registrar.RegisterAsync(
            new VisitRegistrationInput(
                command.PatientId,
                booking is null ? VisitLocationType.Walkin : VisitLocationType.Home,
                command.TreatmentNo,
                command.ReferringDoctorId,
                command.PayerId,
                command.ContractTierId,
                command.InsuranceId,
                command.DiagnosisId,
                booking?.Id,
                null,
                command.Urgent,
                command.Biohazard,
                command.CollectedOutside,
                command.Lines,
                command.PreparationAnswers,
                command.Discount,
                command.ConfirmFrequencyWarnings,
                RegistrationRules.UtcOffset(command.UtcOffsetMinutes),
                booking?.PhlebotomistUserId),
            actor,
            branch,
            now,
            cancellationToken);

        visits.Add(visit);
        booking?.Convert(visit.Id, visit.Accession, visit.PatientId, actor, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new VisitRegisteredResponse(visit.Id, visit.Accession, visit.Tubes.Count, plan.Warnings);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Edit)]
public sealed record AddVisitLinesCommand(Guid VisitId, IReadOnlyList<OrderLineRequest> Lines, IReadOnlyList<PreparationAnswerRequest>? PreparationAnswers, bool ConfirmFrequencyWarnings) : ICommand<VisitLinesAddedResponse>;

public sealed class AddVisitLinesValidator : AbstractValidator<AddVisitLinesCommand>
{
    public AddVisitLinesValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        OrderShape.Lines(this, command => command.Lines);
        OrderShape.Answers(this, command => command.PreparationAnswers);
    }
}

/// <summary>V2: the planner against the visit's live lines (duplicates, rules, frequency outside this visit), then BR-02 on the saved tubes.</summary>
public sealed class AddVisitLinesHandler(
    IVisitRepository visits,
    IPatientLookup patients,
    ICatalogueLookup catalogue,
    IOrderRulesLookup rules,
    IPriceLookup prices,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<AddVisitLinesCommand, VisitLinesAddedResponse>
{
    public async Task<VisitLinesAddedResponse> Handle(AddVisitLinesCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        var now = clock.UtcNow;
        var visit = await visits.GetForUpdateAsync(command.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        if (visit.Status != VisitStatus.Open)
        {
            throw new InvalidStateTransitionException(RegistrationCodes.VisitNotOpen, "visit", visit.Status.ToString(), "add services");
        }

        var patient = await patients.GetAsync(visit.PatientId, cancellationToken);
        var planner = new OrderPlanner(catalogue, rules, prices, visits);
        var plan = await planner.PlanAsync(
            new OrderContext(
                patient is null ? null : new OrderPatient(patient.Id, patient.Gender),
                visit.PayerId,
                visit.ContractTierId,
                null,
                command.Lines,
                visit.LiveServiceIds,
                visit.Id,
                now,
                visit.UtcOffsetMinutes,
                ExistingTests: visit.LiveTestHolders()),
            cancellationToken);
        plan.ThrowIfRefused(command.ConfirmFrequencyWarnings);

        var items = plan.Items;
        var applicable = await rules.PreparationsForAsync(
            [.. visit.LiveServiceIds.Concat(items.SelectMany(item => item.AllServiceIds)).Distinct()],
            patient?.Gender switch { "Male" => PreparationGender.Male, "Female" => PreparationGender.Female, _ => null },
            cancellationToken);
        var answers = RegistrationRules.Answers(command.PreparationAnswers, applicable);
        var tubesBefore = visit.Tubes.Count;
        var samples = await RegistrationRules.SampleNumbersAsync(numbers, visit.CountNewTubes(Visit.TubeNeedsOf(items)), now, cancellationToken);

        visit.AddLines(items, answers, samples, actor, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new VisitLinesAddedResponse(items.Count, visit.Tubes.Count - tubesBefore, plan.Warnings);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Edit)]
public sealed record ReleaseReservedLinesCommand(Guid VisitId, IReadOnlyList<Guid> LineIds) : ICommand;

public sealed class ReleaseReservedLinesValidator : AbstractValidator<ReleaseReservedLinesCommand>
{
    public ReleaseReservedLinesValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.LineIds).NotNull().Must(ids => ids is { Count: >= 1 and <= Visit.MaxLines });
        RuleForEach(command => command.LineIds).NotEmpty();
    }
}

/// <summary>V3: Reserved → Ordered; tube specs and expected dates are resolved from the catalogue at release time (BR-02, BR-13).</summary>
public sealed class ReleaseReservedLinesHandler(
    IVisitRepository visits,
    ICatalogueLookup catalogue,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<ReleaseReservedLinesCommand>
{
    public async Task Handle(ReleaseReservedLinesCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        var now = clock.UtcNow;
        var visit = await visits.GetForUpdateAsync(command.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);

        var lines = visit.LinesAffectedBy(command.LineIds);
        var services = (await catalogue.GetServicesAsync([.. lines.Select(line => line.ServiceId).Distinct()], cancellationToken)).ToDictionary(service => service.Id);
        var testIds = lines.SelectMany(line => line.TestIds).Distinct().ToList();
        var tests = testIds.Count == 0 ? new Dictionary<Guid, CatalogueTestInfo>() : (await catalogue.GetTestsAsync(testIds, cancellationToken)).ToDictionary(test => test.Id);
        var groupIds = services.Values.Where(service => service.ClinicalGroupId.HasValue).Select(service => service.ClinicalGroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0 ? new Dictionary<Guid, CatalogueClinicalGroupInfo>() : (await catalogue.GetClinicalGroupsAsync(groupIds, cancellationToken)).ToDictionary(group => group.Id);
        var typeIds = services.Values.Where(service => service.SampleTypeId.HasValue).Select(service => service.SampleTypeId!.Value).Distinct().ToList();
        var sampleTypes = typeIds.Count == 0 ? new Dictionary<Guid, CatalogueSampleTypeInfo>() : (await catalogue.GetSampleTypesAsync(typeIds, cancellationToken)).ToDictionary(type => type.Id);

        var releases = new List<LineRelease>();
        foreach (var line in lines)
        {
            if (!services.TryGetValue(line.ServiceId, out var service))
            {
                releases.Add(new LineRelease(line.Id, null, line.ExpectedResultAtUtc));
                continue;
            }

            var expected = line.Kind == VisitLineKind.Package
                ? (service.TurnaroundHours > 0
                    ? TurnaroundCalculator.ExpectedAt(now, service.TurnaroundHours, TurnaroundCalculator.EveryDay, visit.UtcOffsetMinutes)
                    : TurnaroundCalculator.Latest(lines.Where(member => member.ParentLineId == line.Id).Select(member =>
                        services.TryGetValue(member.ServiceId, out var memberService) ? OrderPlanner.ExpectedFor(memberService, member.TestIds, tests, now, visit.UtcOffsetMinutes) : member.ExpectedResultAtUtc)))
                : OrderPlanner.ExpectedFor(service, line.TestIds, tests, now, visit.UtcOffsetMinutes);
            releases.Add(new LineRelease(line.Id, OrderPlanner.TubeSpecOf(service, groups, sampleTypes), expected));
        }

        var needs = releases.Where(release => lines.First(line => line.Id == release.LineId).Kind != VisitLineKind.Package).Select(release => release.Tube);
        var samples = await RegistrationRules.SampleNumbersAsync(numbers, visit.CountNewTubes(needs), now, cancellationToken);
        visit.ReleaseReserved(releases, samples, actor, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Cancel)]
public sealed record CancelVisitLinesCommand(Guid VisitId, IReadOnlyList<Guid> LineIds, Guid? ReasonId, string? Reason) : ICommand;

public sealed class CancelVisitLinesValidator : AbstractValidator<CancelVisitLinesCommand>
{
    public CancelVisitLinesValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.LineIds).NotNull().Must(ids => ids is { Count: >= 1 and <= Visit.MaxLines });
        RuleForEach(command => command.LineIds).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(Visit.MaxReasonLength);
        RuleFor(command => command).Must(command => command.ReasonId is not null || !string.IsNullOrWhiteSpace(command.Reason)).WithName("reason").WithMessage("Choose a cancellation reason or type one.");
    }
}

/// <summary>V4: Cancel right (REG-061), no verified result (REG-106), reason; one VisitLineCancelled per line.</summary>
public sealed class CancelVisitLinesHandler(
    IVisitRepository visits,
    IResultStatusLookup results,
    IOrderRulesLookup rules,
    IUserPrivileges privileges,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<CancelVisitLinesCommand>
{
    public async Task Handle(CancelVisitLinesCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        if (!privileges.SpecialRights.CanCancel)
        {
            throw new DomainException(RegistrationCodes.CancelRightRequired, "Cancelling services needs the Cancel special right.", DomainFault.Refused);
        }

        var visit = await visits.GetForUpdateAsync(command.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);

        var affected = visit.LinesAffectedBy(command.LineIds);
        if (await results.HasVerifiedResultsAsync([.. affected.Select(line => line.Id)], cancellationToken))
        {
            foreach (var line in affected)
            {
                if (await results.HasVerifiedResultsAsync([line.Id], cancellationToken))
                {
                    throw new DomainException(RegistrationCodes.VerifiedResults, $"'{line.ServiceCode}' has verified results and cannot be cancelled.", DomainFault.Conflict);
                }
            }

            throw new DomainException(RegistrationCodes.VerifiedResults, "Services with verified results cannot be cancelled.", DomainFault.Conflict);
        }

        var reason = await RegistrationRules.ReasonTextAsync(rules, command.ReasonId, command.Reason, ReasonKind.Cancellation, RegistrationCodes.CancellationReasonRequired, cancellationToken);
        visit.CancelLines(command.LineIds, command.ReasonId, reason, actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Edit)]
public sealed record UpdateVisitDetailsCommand(Guid VisitId, string? TreatmentNo, Guid? ReferringDoctorId, string? InsuranceId, bool Urgent, bool Biohazard, bool CollectedOutside) : ICommand;

public sealed class UpdateVisitDetailsValidator : AbstractValidator<UpdateVisitDetailsCommand>
{
    public UpdateVisitDetailsValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.TreatmentNo).MaximumLength(Visit.MaxTreatmentNoLength);
        RuleFor(command => command.InsuranceId).MaximumLength(Visit.MaxInsuranceIdLength);
    }
}

/// <summary>V5: Open only; doctor rules of the visit's branch.</summary>
public sealed class UpdateVisitDetailsHandler(IVisitRepository visits, IDoctorLookup doctors, ITenantSettings settings, IUnitOfWork unitOfWork) : ICommandHandler<UpdateVisitDetailsCommand>
{
    public async Task Handle(UpdateVisitDetailsCommand command, CancellationToken cancellationToken)
    {
        var visit = await visits.GetForUpdateAsync(command.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        if (command.ReferringDoctorId != visit.ReferringDoctorId)
        {
            await RegistrationRules.CheckDoctorAsync(doctors, settings, command.ReferringDoctorId, visit.BranchId, cancellationToken);
        }

        visit.UpdateDetails(new VisitDetails(command.TreatmentNo, command.ReferringDoctorId, command.InsuranceId, command.Urgent, command.Biohazard, command.CollectedOutside));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Edit)]
public sealed record SetVisitDiscountCommand(Guid VisitId, VisitDiscountRequest? Discount, string? Reason) : ICommand;

public sealed class SetVisitDiscountValidator : AbstractValidator<SetVisitDiscountCommand>
{
    public SetVisitDiscountValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(Visit.MaxReasonLength);
        OrderShape.Discount(this, command => command.Discount);
    }
}

/// <summary>V6: Discount reason of kind Discount and the actor's cap (INV-REG-005); null clears it.</summary>
public sealed class SetVisitDiscountHandler(
    IVisitRepository visits,
    IOrderRulesLookup rules,
    ITenantSettings settings,
    IUserPrivileges privileges,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<SetVisitDiscountCommand>
{
    public async Task Handle(SetVisitDiscountCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        var visit = await visits.GetForUpdateAsync(command.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        var discount = await RegistrationRules.DiscountAsync(rules, settings, privileges, command.Discount, cancellationToken);
        visit.SetDiscount(discount, command.Reason, actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
