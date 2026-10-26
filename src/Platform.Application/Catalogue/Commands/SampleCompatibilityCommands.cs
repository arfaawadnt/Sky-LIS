using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Commands;

// Compatibility groups of samples (change request LIS-CR-015, note M4). The aggregate checks its own members; a pair in at
// most one active group of the laboratory (CAT-284) is checked here against the other groups.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateSampleCompatibilityGroupCommand(string Code, string NameEn, string NameAr, IReadOnlyList<CompatibilityMemberItem> Members) : ICommand<CreatedResource>;

public sealed class CreateSampleCompatibilityGroupValidator : AbstractValidator<CreateSampleCompatibilityGroupCommand>
{
    public CreateSampleCompatibilityGroupValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(SampleCompatibilityGroup.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Members).NotNull();
        RuleFor(command => command.Members.Count).LessThanOrEqualTo(SampleCompatibilityGroup.MaxMembers).When(command => command.Members is not null);
        RuleForEach(command => command.Members).SetValidator(new CompatibilityMemberItemValidator());
    }
}

public sealed class CompatibilityMemberItemValidator : AbstractValidator<CompatibilityMemberItem>
{
    public CompatibilityMemberItemValidator()
    {
        RuleFor(item => item.SampleTypeId).NotEmpty();
        RuleFor(item => item.SampleConditionId).NotEqual(Guid.Empty).When(item => item.SampleConditionId is not null);
    }
}

public sealed class CreateSampleCompatibilityGroupHandler(ISampleCompatibilityGroupRepository groups, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateSampleCompatibilityGroupCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateSampleCompatibilityGroupCommand command, CancellationToken cancellationToken)
    {
        var code = SampleCompatibilityGroup.CleanCode(command.Code);
        if (await groups.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(SampleCompatibilityCodes.CodeExists, $"A compatibility group with code '{code}' already exists.", DomainFault.Conflict);
        }

        var members = SampleCompatibilityRules.Pairs(command.Members);
        var sampleTypes = await groups.SampleTypesAsync([.. members.Select(pair => pair.SampleTypeId).Distinct()], cancellationToken);
        var group = SampleCompatibilityGroup.Create(code, new LocalizedText(command.NameEn, command.NameAr), members, sampleTypes, clock.UtcNow);
        await SampleCompatibilityRules.RequireNoOtherActiveHolderAsync(groups, group, cancellationToken);
        groups.Add(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(group.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateSampleCompatibilityGroupCommand(Guid GroupId, string NameEn, string NameAr, IReadOnlyList<CompatibilityMemberItem> Members) : ICommand;

public sealed class UpdateSampleCompatibilityGroupValidator : AbstractValidator<UpdateSampleCompatibilityGroupCommand>
{
    public UpdateSampleCompatibilityGroupValidator()
    {
        RuleFor(command => command.GroupId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Members).NotNull();
        RuleFor(command => command.Members.Count).LessThanOrEqualTo(SampleCompatibilityGroup.MaxMembers).When(command => command.Members is not null);
        RuleForEach(command => command.Members).SetValidator(new CompatibilityMemberItemValidator());
    }
}

public sealed class UpdateSampleCompatibilityGroupHandler(ISampleCompatibilityGroupRepository groups, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdateSampleCompatibilityGroupCommand>
{
    public async Task Handle(UpdateSampleCompatibilityGroupCommand command, CancellationToken cancellationToken)
    {
        var group = await groups.GetForUpdateAsync(command.GroupId, cancellationToken)
            ?? throw new DomainException(SampleCompatibilityCodes.NotFound, "Compatibility group not found.", DomainFault.NotFound);

        var members = SampleCompatibilityRules.Pairs(command.Members);
        var sampleTypes = await groups.SampleTypesAsync([.. members.Select(pair => pair.SampleTypeId).Distinct()], cancellationToken);
        group.Update(new LocalizedText(command.NameEn, command.NameAr), members, sampleTypes, clock.UtcNow);
        await SampleCompatibilityRules.RequireNoOtherActiveHolderAsync(groups, group, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Activate / deactivate (<c>catalogue.edit</c>); a reactivation is refused while a member is in another active group (CAT-284).</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record SetSampleCompatibilityGroupActiveCommand(Guid GroupId, bool Active) : ICommand;

public sealed class SetSampleCompatibilityGroupActiveValidator : AbstractValidator<SetSampleCompatibilityGroupActiveCommand>
{
    public SetSampleCompatibilityGroupActiveValidator() => RuleFor(command => command.GroupId).NotEmpty();
}

public sealed class SetSampleCompatibilityGroupActiveHandler(ISampleCompatibilityGroupRepository groups, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetSampleCompatibilityGroupActiveCommand>
{
    public async Task Handle(SetSampleCompatibilityGroupActiveCommand command, CancellationToken cancellationToken)
    {
        var group = await groups.GetForUpdateAsync(command.GroupId, cancellationToken)
            ?? throw new DomainException(SampleCompatibilityCodes.NotFound, "Compatibility group not found.", DomainFault.NotFound);

        if (command.Active)
        {
            group.Reactivate(clock.UtcNow);
            await SampleCompatibilityRules.RequireNoOtherActiveHolderAsync(groups, group, cancellationToken);
        }
        else
        {
            group.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The rules of the module that need more than one group.</summary>
internal static class SampleCompatibilityRules
{
    public static IReadOnlyList<CompatibleSamplePair> Pairs(IReadOnlyList<CompatibilityMemberItem> members) =>
        [.. (members ?? []).Select(member => new CompatibleSamplePair(member.SampleTypeId, member.SampleConditionId))];

    /// <summary>CAT-284: an active group holds no pair another active group of the laboratory holds.</summary>
    public static async Task RequireNoOtherActiveHolderAsync(ISampleCompatibilityGroupRepository groups, SampleCompatibilityGroup group, CancellationToken cancellationToken)
    {
        if (!group.IsActive)
        {
            return;
        }

        if (await groups.FindActiveHolderAsync([.. group.Pairs], group.Id, cancellationToken) is { } conflict)
        {
            var pair = conflict.SampleConditionCode is null ? conflict.SampleTypeCode : $"{conflict.SampleTypeCode} · {conflict.SampleConditionCode}";
            throw new DomainException(
                SampleCompatibilityCodes.MemberInOtherGroup,
                $"{pair} is already in the active compatibility group '{conflict.GroupCode}'; a sample belongs to one active group only.",
                DomainFault.Conflict);
        }
    }
}
