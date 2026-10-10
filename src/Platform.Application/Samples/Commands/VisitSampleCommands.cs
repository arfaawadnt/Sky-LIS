using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Samples;
using Platform.Domain.Authorization;
using Platform.Domain.Samples;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Samples.Commands;

// CP-14 "collect/receive all samples of an accession": T2 / T3 applied to every live sample of the visit for which the
// transition is allowed; the others are reported as unchanged (intake §7, bulk actions). One commit per command.

[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.Create)]
public sealed record CollectVisitSamplesCommand(Guid VisitId) : ICommand<VisitSamplesActionResult>;

public sealed class CollectVisitSamplesValidator : AbstractValidator<CollectVisitSamplesCommand>
{
    public CollectVisitSamplesValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
    }
}

public sealed class CollectVisitSamplesHandler(
    ISampleRepository samples,
    ITenantSettings settings,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<CollectVisitSamplesCommand, VisitSamplesActionResult>
{
    public async Task<VisitSamplesActionResult> Handle(CollectVisitSamplesCommand command, CancellationToken cancellationToken)
    {
        var actor = SamplesActor.Of(user);
        var branch = BranchScope.Require(user);
        var visitSamples = await samples.ListLiveByVisitForUpdateAsync(command.VisitId, cancellationToken);
        if (visitSamples.Count == 0)
        {
            throw SamplesNotFound.Visit();
        }

        var autoReceive = await settings.GetAsync(SettingKeys.AutoReceive, cancellationToken);
        var now = clock.UtcNow;
        var changed = new List<string>();
        foreach (var sample in visitSamples.Where(sample => sample.Status == SampleStatus.Ordered))
        {
            sample.Collect(actor, branch, now);
            if (autoReceive)
            {
                sample.Receive(actor, branch, now);
            }

            changed.Add(sample.SampleNumber);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new VisitSamplesActionResult(command.VisitId, changed.Count, visitSamples.Count - changed.Count, changed);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.Create)]
public sealed record ReceiveVisitSamplesCommand(Guid VisitId) : ICommand<VisitSamplesActionResult>;

public sealed class ReceiveVisitSamplesValidator : AbstractValidator<ReceiveVisitSamplesCommand>
{
    public ReceiveVisitSamplesValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
    }
}

public sealed class ReceiveVisitSamplesHandler(
    ISampleRepository samples,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<ReceiveVisitSamplesCommand, VisitSamplesActionResult>
{
    public async Task<VisitSamplesActionResult> Handle(ReceiveVisitSamplesCommand command, CancellationToken cancellationToken)
    {
        var actor = SamplesActor.Of(user);
        var branch = BranchScope.Require(user);
        var visitSamples = await samples.ListLiveByVisitForUpdateAsync(command.VisitId, cancellationToken);
        if (visitSamples.Count == 0)
        {
            throw SamplesNotFound.Visit();
        }

        var now = clock.UtcNow;
        var changed = new List<string>();
        foreach (var sample in visitSamples.Where(sample => sample.Status is SampleStatus.Ordered or SampleStatus.Collected))
        {
            sample.Receive(actor, branch, now);
            changed.Add(sample.SampleNumber);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new VisitSamplesActionResult(command.VisitId, changed.Count, visitSamples.Count - changed.Count, changed);
    }
}
