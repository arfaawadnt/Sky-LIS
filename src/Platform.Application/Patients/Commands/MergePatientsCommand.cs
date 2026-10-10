using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Domain.Authorization;
using Platform.Domain.Patients;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Patients.Commands;

/// <summary>
/// T3 Merge (CP-11 "merge duplicates (right)", REG-003): <paramref name="MergedPatientId"/> becomes Merged and points
/// to <paramref name="SurvivorId"/>, which keeps its number. Visits, results and payments follow through the policies
/// of their modules on <see cref="PatientsMerged"/>.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Patients, PermissionAction.Manage)]
public sealed record MergePatientsCommand(Guid SurvivorId, Guid MergedPatientId, string Reason, bool ConfirmGenderMismatch) : ICommand;

public sealed class MergePatientsValidator : AbstractValidator<MergePatientsCommand>
{
    public MergePatientsValidator()
    {
        RuleFor(command => command.SurvivorId).NotEmpty();
        RuleFor(command => command.MergedPatientId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(Patient.MaxReasonLength);
    }
}

public sealed class MergePatientsHandler(IPatientRepository patients, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<MergePatientsCommand>
{
    public async Task Handle(MergePatientsCommand command, CancellationToken cancellationToken)
    {
        var actor = PatientActor.Of(user);
        var survivor = await patients.GetForUpdateAsync(command.SurvivorId, cancellationToken)
            ?? throw new DomainException(PatientCodes.PatientNotFound, "The surviving patient file was not found.", DomainFault.NotFound);
        var merged = await patients.GetForUpdateAsync(command.MergedPatientId, cancellationToken)
            ?? throw new DomainException(PatientCodes.PatientNotFound, "The patient file to merge was not found.", DomainFault.NotFound);

        merged.MergeInto(survivor, actor, command.Reason, command.ConfirmGenderMismatch, clock.UtcNow);   // self, states, genders, reason; the event
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
