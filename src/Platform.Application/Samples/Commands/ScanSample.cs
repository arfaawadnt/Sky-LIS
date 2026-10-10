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

// T2 / T3 by barcode (CP-14 scan box). The validator checks shape; the aggregate decides (pitfall P-10).

/// <summary><paramref name="Mode"/> is <c>Collect</c> or <c>Receive</c>; the response is the sample after the scan, for the feedback strip.</summary>
[RequirePermissionPolicy(PermissionCatalog.Samples, PermissionAction.Create)]
public sealed record ScanSampleCommand(string SampleNumber, string Mode) : ICommand<SampleDetail>;

public sealed class ScanSampleValidator : AbstractValidator<ScanSampleCommand>
{
    public ScanSampleValidator()
    {
        RuleFor(command => command.SampleNumber).NotEmpty().MaximumLength(Sample.MaxSampleNumberLength);
        RuleFor(command => command.Mode).NotEmpty().IsEnumName(typeof(ScanMode), caseSensitive: false);
    }
}

public sealed class ScanSampleHandler(
    ISampleRepository samples,
    ITenantSettings settings,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<ScanSampleCommand, SampleDetail>
{
    public async Task<SampleDetail> Handle(ScanSampleCommand command, CancellationToken cancellationToken)
    {
        var actor = SamplesActor.Of(user);
        var branch = BranchScope.Require(user);
        var number = Sample.CleanNumber(command.SampleNumber);
        var sample = await samples.GetByNumberForUpdateAsync(number, cancellationToken) ?? throw SamplesNotFound.Sample();

        var now = clock.UtcNow;
        if (Enum.Parse<ScanMode>(command.Mode, ignoreCase: true) == ScanMode.Collect)
        {
            sample.Collect(actor, branch, now);   // SMP-010 unless Ordered
            if (await settings.GetAsync(SettingKeys.AutoReceive, cancellationToken))
            {
                sample.Receive(actor, branch, now);   // "marked Received when collected" (intake §16 Q2)
            }
        }
        else
        {
            sample.Receive(actor, branch, now);   // records the collection first when the tube was not scanned at phlebotomy
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await samples.GetDetailAsync(sample.Id, cancellationToken) ?? throw SamplesNotFound.Sample();
    }
}
