using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Samples;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Samples.Commands;

// CP-16 Reject / unreject (T4–T6, WF-05 steps 3–4, BR-18). Validators check shape; the aggregate decides.

[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.Edit)]
public sealed record RejectSampleCommand(Guid SampleId, Guid ReasonId, string? Note) : ICommand;

public sealed class RejectSampleValidator : AbstractValidator<RejectSampleCommand>
{
    public RejectSampleValidator()
    {
        RuleFor(command => command.SampleId).NotEmpty();
        RuleFor(command => command.ReasonId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(Sample.MaxNoteLength);
    }
}

public sealed class RejectSampleHandler(
    ISampleRepository samples,
    IRejectionReasonLookup reasons,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<RejectSampleCommand>
{
    public async Task Handle(RejectSampleCommand command, CancellationToken cancellationToken)
    {
        var actor = SamplesActor.Of(user);
        var branch = BranchScope.Require(user);
        var sample = await samples.GetForUpdateAsync(command.SampleId, cancellationToken) ?? throw SamplesNotFound.Sample();

        // INV-SMP-004: a catalogue reason of kind Rejection, decided here with its code; while the catalogue table is not
        // installed the id is kept as given (intake §13).
        var check = await reasons.FindAsync(command.ReasonId, cancellationToken);
        RejectionReason reason;
        if (check.CatalogueAvailable)
        {
            if (check.Reason is not { IsUsableForRejection: true } row)
            {
                throw new DomainException(SamplesCodes.RejectionReasonUnknown, "The reason is not an active rejection reason.", DomainFault.Validation);
            }

            reason = new RejectionReason(row.Id, row.NameEn, row.NameAr);
        }
        else
        {
            reason = new RejectionReason(command.ReasonId, null, null);
        }

        sample.Reject(reason, command.Note, actor, branch, clock.UtcNow);   // SMP-012 unless Collected/Received
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.Edit)]
public sealed record UnrejectSampleCommand(Guid SampleId, string? Note) : ICommand;

public sealed class UnrejectSampleValidator : AbstractValidator<UnrejectSampleCommand>
{
    public UnrejectSampleValidator()
    {
        RuleFor(command => command.SampleId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(Sample.MaxNoteLength);
    }
}

public sealed class UnrejectSampleHandler(
    ISampleRepository samples,
    ITenantSettings settings,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<UnrejectSampleCommand>
{
    public async Task Handle(UnrejectSampleCommand command, CancellationToken cancellationToken)
    {
        var actor = SamplesActor.Of(user);
        var branch = BranchScope.Require(user);
        var sample = await samples.GetForUpdateAsync(command.SampleId, cancellationToken) ?? throw SamplesNotFound.Sample();

        // BR-18: reinstating the same barcode is allowed only when the tenant does not require a new one.
        var reinstatementAllowed = !await settings.GetAsync(SettingKeys.NewBarcodeOnReject, cancellationToken);
        sample.Unreject(reinstatementAllowed, command.Note, actor, branch, clock.UtcNow);   // SMP-013 / SMP-016
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T6: the response is the new sample (its label is printed next).</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.Edit)]
public sealed record ReplaceSampleCommand(Guid SampleId, string? Note) : ICommand<CreatedResource>;

public sealed class ReplaceSampleValidator : AbstractValidator<ReplaceSampleCommand>
{
    public ReplaceSampleValidator()
    {
        RuleFor(command => command.SampleId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(Sample.MaxNoteLength);
    }
}

public sealed class ReplaceSampleHandler(
    ISampleRepository samples,
    IReferenceNumbers numbers,
    ITenantSettings settings,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<ReplaceSampleCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(ReplaceSampleCommand command, CancellationToken cancellationToken)
    {
        var actor = SamplesActor.Of(user);
        var branch = BranchScope.Require(user);
        var sample = await samples.GetForUpdateAsync(command.SampleId, cancellationToken) ?? throw SamplesNotFound.Sample();
        sample.EnsureReplaceable();   // SMP-013, refused before a number is drawn from the counter

        var now = clock.UtcNow;
        var number = await numbers.NextAsync(Sample.NumberKind, Sample.NumberPrefix, now, cancellationToken);
        var replacement = sample.Replace(number, command.Note, actor, branch, now);
        replacement.AutoAdvance(
            await settings.GetAsync(SettingKeys.AutoCollect, cancellationToken),
            await settings.GetAsync(SettingKeys.AutoReceive, cancellationToken),
            actor,
            branch,
            now);
        samples.Add(replacement);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(replacement.Id);
    }
}
