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
using Platform.Domain.Registration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration.Commands;

// Batch registration (CP-08 group, CP-09 lab-to-lab; WF-04): T1 register, T2 add members. One visit per member, every
// member through the same registrar as a walk-in (same rules), the batch and its visits in one unit of work.

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Create)]
public sealed record RegisterBatchCommand(
    string Kind,
    Guid? PayerId,
    Guid? ContractTierId,
    Guid? SendingLabId,
    Guid? ReferringDoctorId,
    string? GroupName,
    string? Notes,
    IReadOnlyList<Guid>? ServiceIds,
    IReadOnlyList<BatchMemberRequest> Members,
    bool ConfirmFrequencyWarnings,
    int? UtcOffsetMinutes) : ICommand<BatchRegisteredResponse>;

public sealed class RegisterBatchValidator : AbstractValidator<RegisterBatchCommand>
{
    public RegisterBatchValidator()
    {
        RuleFor(command => command.Kind).NotEmpty().IsEnumName(typeof(BatchKind), caseSensitive: false);
        RuleFor(command => command.GroupName).MaximumLength(Batch.MaxGroupNameLength);
        RuleFor(command => command.Notes).MaximumLength(Batch.MaxNotesLength);
        RuleFor(command => command.ServiceIds).Must(ids => ids is null || ids.Count <= Visit.MaxLines).WithMessage($"At most {Visit.MaxLines} services.");
        RuleFor(command => command.Members).NotNull().Must(members => members is { Count: >= 1 and <= Batch.MaxMembers }).WithMessage($"Add 1 to {Batch.MaxMembers} members.");
        RuleForEach(command => command.Members).ChildRules(member =>
        {
            member.RuleFor(value => value.PatientId).NotEmpty();
            member.RuleFor(value => value.ServiceIds).Must(ids => ids is null || ids.Count <= Visit.MaxLines);
        });
        RuleFor(command => command.UtcOffsetMinutes).InclusiveBetween(-Visit.MaxUtcOffsetMinutes, Visit.MaxUtcOffsetMinutes).When(command => command.UtcOffsetMinutes is not null);
    }
}

/// <summary>T1: the header (group contract or sending lab), one visit per member (REG-032/033 and every V1 rule, refusals prefixed with the row), one commit.</summary>
public sealed class RegisterBatchHandler(
    IBatchRepository batches,
    IVisitRepository visits,
    ISendingLabLookup sendingLabs,
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
    IClock clock) : ICommandHandler<RegisterBatchCommand, BatchRegisteredResponse>
{
    public async Task<BatchRegisteredResponse> Handle(RegisterBatchCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        var branch = BranchScope.Require(user);
        var now = clock.UtcNow;
        var kind = Enum.Parse<BatchKind>(command.Kind, ignoreCase: true);

        var header = await BatchMembers.HeaderAsync(kind, command, sendingLabs, payers, doctors, settings, branch, cancellationToken);
        var number = await numbers.NextAsync(Batch.NumberKind, Batch.NumberPrefix, now, cancellationToken);
        var batch = Batch.Register(number, branch, header, command.Members.Count, actor, now);

        var registrar = new VisitRegistrar(new RegistrarPorts(visits, patients, payers, doctors, catalogue, rules, prices, settings, privileges, numbers));
        var registered = await BatchMembers.RegisterAsync(registrar, visits, batch, command.Members, command.ServiceIds, command.ConfirmFrequencyWarnings, RegistrationRules.UtcOffset(command.UtcOffsetMinutes), actor, branch, now, cancellationToken);

        batches.Add(batch);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BatchRegisteredResponse(batch.Id, batch.Number, registered);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Create)]
public sealed record AddBatchMembersCommand(Guid BatchId, IReadOnlyList<BatchMemberRequest> Members, IReadOnlyList<Guid>? ServiceIds, bool ConfirmFrequencyWarnings, int? UtcOffsetMinutes) : ICommand<BatchRegisteredResponse>;

public sealed class AddBatchMembersValidator : AbstractValidator<AddBatchMembersCommand>
{
    public AddBatchMembersValidator()
    {
        RuleFor(command => command.BatchId).NotEmpty();
        RuleFor(command => command.ServiceIds).Must(ids => ids is null || ids.Count <= Visit.MaxLines).WithMessage($"At most {Visit.MaxLines} services.");
        RuleFor(command => command.Members).NotNull().Must(members => members is { Count: >= 1 and <= Batch.MaxMembers }).WithMessage($"Add 1 to {Batch.MaxMembers} members.");
        RuleForEach(command => command.Members).ChildRules(member => member.RuleFor(value => value.PatientId).NotEmpty());
        RuleFor(command => command.UtcOffsetMinutes).InclusiveBetween(-Visit.MaxUtcOffsetMinutes, Visit.MaxUtcOffsetMinutes).When(command => command.UtcOffsetMinutes is not null);
    }
}

/// <summary>T2: members added to an existing batch on its contract; a patient already in the batch is refused (REG-033).</summary>
public sealed class AddBatchMembersHandler(
    IBatchRepository batches,
    IVisitRepository visits,
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
    IClock clock) : ICommandHandler<AddBatchMembersCommand, BatchRegisteredResponse>
{
    public async Task<BatchRegisteredResponse> Handle(AddBatchMembersCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        var branch = BranchScope.Require(user);
        var now = clock.UtcNow;
        var batch = await batches.GetForUpdateAsync(command.BatchId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.BatchNotFound, "Batch not found.", DomainFault.NotFound);

        var existing = (await visits.ListByBatchAsync(batch.Id, cancellationToken)).Select(row => row.PatientId).ToHashSet();
        var clash = command.Members.FirstOrDefault(member => existing.Contains(member.PatientId));
        if (clash is not null)
        {
            throw new DomainException(RegistrationCodes.BatchDuplicateMember, "A patient of these rows is already a member of the batch.", DomainFault.Validation);
        }

        await RegistrationRules.RequireContractAsync(payers, batch.PayerId, batch.ContractTierId, cancellationToken);
        var registrar = new VisitRegistrar(new RegistrarPorts(visits, patients, payers, doctors, catalogue, rules, prices, settings, privileges, numbers));
        var registered = await BatchMembers.RegisterAsync(registrar, visits, batch, command.Members, command.ServiceIds, command.ConfirmFrequencyWarnings, RegistrationRules.UtcOffset(command.UtcOffsetMinutes), actor, branch, now, cancellationToken);

        batch.AddMembers(command.Members.Count, actor, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BatchRegisteredResponse(batch.Id, batch.Number, registered);
    }
}

/// <summary>The shared steps of T1/T2.</summary>
internal static class BatchMembers
{
    public static async Task<BatchHeader> HeaderAsync(
        BatchKind kind,
        RegisterBatchCommand command,
        ISendingLabLookup sendingLabs,
        IPayerLookup payers,
        IDoctorLookup doctors,
        ITenantSettings settings,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        if (kind == BatchKind.LabToLab)
        {
            var lab = command.SendingLabId is { } labId ? await sendingLabs.GetAsync(labId, cancellationToken) : null;
            if (lab is null || !lab.IsActive)
            {
                throw new DomainException(RegistrationCodes.SendingLabInvalid, "The sending laboratory does not exist or is inactive.", DomainFault.Validation);
            }

            await RegistrationRules.RequireContractAsync(payers, lab.PayerId, lab.ContractTierId, cancellationToken);
            var doctorId = command.ReferringDoctorId ?? lab.DoctorId;
            await RegistrationRules.CheckDoctorAsync(doctors, settings, doctorId, branchId, cancellationToken);
            return new BatchHeader(kind, lab.PayerId, lab.ContractTierId, lab.Id, doctorId, null, lab.CourierEn, lab.CourierAr, command.Notes);
        }

        if (command.PayerId is not { } payerId || command.ContractTierId is not { } tierId)
        {
            throw new DomainException(RegistrationCodes.BatchContractRequired, "A group batch needs a payer and a contract tier.", DomainFault.Validation);
        }

        if (command.ServiceIds is not { Count: > 0 })
        {
            throw new DomainException(RegistrationCodes.LinesRequired, "Choose the services of the group.", DomainFault.Validation);
        }

        await RegistrationRules.RequireContractAsync(payers, payerId, tierId, cancellationToken);
        await RegistrationRules.CheckDoctorAsync(doctors, settings, command.ReferringDoctorId, branchId, cancellationToken);
        return new BatchHeader(kind, payerId, tierId, null, command.ReferringDoctorId, command.GroupName, null, null, command.Notes);
    }

    public static async Task<IReadOnlyList<BatchMemberVisit>> RegisterAsync(
        VisitRegistrar registrar,
        IVisitRepository visits,
        Batch batch,
        IReadOnlyList<BatchMemberRequest> members,
        IReadOnlyList<Guid>? groupServiceIds,
        bool confirmFrequencyWarnings,
        int utcOffsetMinutes,
        Guid actorUserId,
        Guid branchId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var duplicate = members.GroupBy(member => member.PatientId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException(RegistrationCodes.BatchDuplicateMember, "The same patient appears twice in the batch.", DomainFault.Validation);
        }

        var registered = new List<BatchMemberVisit>();
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            var serviceIds = batch.Kind == BatchKind.Group ? groupServiceIds ?? [] : member.ServiceIds ?? [];
            try
            {
                if (serviceIds.Count == 0)
                {
                    throw new DomainException(RegistrationCodes.LinesRequired, "Choose at least one service.", DomainFault.Validation);
                }

                var (visit, _) = await registrar.RegisterAsync(
                    new VisitRegistrationInput(
                        member.PatientId,
                        batch.Kind == BatchKind.LabToLab ? VisitLocationType.LabToLab : VisitLocationType.Batch,
                        null,
                        batch.ReferringDoctorId,
                        batch.PayerId,
                        batch.ContractTierId,
                        null,
                        null,
                        null,
                        batch.Id,
                        false,
                        false,
                        batch.Kind == BatchKind.LabToLab && member.CollectedOutside,
                        [.. serviceIds.Distinct().Select(id => new OrderLineRequest(id, false))],
                        null,
                        null,
                        confirmFrequencyWarnings,
                        utcOffsetMinutes,
                        null,
                        HeaderChecked: true),
                    actorUserId,
                    branchId,
                    nowUtc,
                    cancellationToken);
                visits.Add(visit);
                registered.Add(new BatchMemberVisit(member.PatientId, visit.Id, visit.Accession));
            }
            catch (DomainException refusal)
            {
                throw new DomainException(refusal.Code, $"Row {index + 1}: {refusal.Message}", refusal.Fault, refusal);
            }
        }

        return registered;
    }
}
