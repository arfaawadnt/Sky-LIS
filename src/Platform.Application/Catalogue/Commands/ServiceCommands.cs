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

// Services (SRS CP-40, CP-42, CP-55; intake LIS-INT-CAT-001 T9–T12). The handler resolves what the aggregate cannot
// read (references, member types, sequences, uniqueness); the aggregate decides the rules of its type.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateServiceCommand(
    string Code,
    string Type,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid? ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    string? Header,
    IReadOnlyList<string>? Aliases,
    string? Precaution,
    string Gender,
    int TurnaroundHours,
    int LabelCount,
    bool PrintAlone,
    bool IsOrderable,
    bool ShowOnWeb,
    decimal? ProfileSum,
    IReadOnlyList<ServiceMemberItem>? Members) : ICommand<CreatedResource>;

public sealed class CreateServiceValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(Service.MaxCodeLength);
        RuleFor(command => command.Type).NotEmpty().IsEnumName(typeof(ServiceType), caseSensitive: false);
        RuleFor(command => command.ShortName).NotEmpty().MaximumLength(Service.MaxShortNameLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.ReportName).MaximumLength(Service.MaxReportNameLength);
        RuleFor(command => command.Loinc).MaximumLength(Service.MaxReferenceCodeLength);
        RuleFor(command => command.Cpt).MaximumLength(Service.MaxReferenceCodeLength);
        RuleFor(command => command.Header).MaximumLength(Service.MaxHeaderLength);
        RuleFor(command => command.Precaution).MaximumLength(Service.MaxPrecautionLength);
        RuleFor(command => command.Gender).NotEmpty().IsEnumName(typeof(GenderRestriction), caseSensitive: false);
        RuleFor(command => command.Aliases!.Count).LessThanOrEqualTo(Service.MaxAliases).When(command => command.Aliases is not null);
        RuleForEach(command => command.Aliases).NotEmpty().MaximumLength(Service.MaxAliasLength);
        RuleFor(command => command.Members!.Count).LessThanOrEqualTo(Service.MaxMembers).When(command => command.Members is not null);
        RuleForEach(command => command.Members).SetValidator(new ServiceMemberItemValidator());
    }
}

public sealed class ServiceMemberItemValidator : AbstractValidator<ServiceMemberItem>
{
    public ServiceMemberItemValidator()
    {
        RuleFor(item => item.Kind).NotEmpty().IsEnumName(typeof(ServiceMemberKind), caseSensitive: false);
        RuleFor(item => item.MemberId).NotEmpty();
        RuleFor(item => item.Order).GreaterThan(0);
    }
}

public sealed class CreateServiceHandler(
    IServiceRepository services,
    ILabTestRepository tests,
    IClinicalGroupRepository groups,
    ISampleTypeRepository sampleTypes,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateServiceCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateServiceCommand command, CancellationToken cancellationToken)
    {
        var code = Service.CleanCode(command.Code);
        if (await services.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(ServiceCodes.CodeExists, $"A service with code '{code}' already exists.", DomainFault.Conflict);
        }

        if (command.ShortcutNo is { } shortcut && await services.ShortcutTakenAsync(shortcut, null, cancellationToken))
        {
            throw new DomainException(ServiceCodes.ShortcutTaken, $"The shortcut number {shortcut} is used by another service.", DomainFault.Conflict);
        }

        await CatalogueAReferences.RequireClinicalGroupAsync(groups, command.ClinicalGroupId, null, cancellationToken);
        await CatalogueAReferences.RequireSampleAsync(sampleTypes, command.SampleTypeId, command.SampleConditionId, null, null, cancellationToken);
        var members = await ServiceMembers.ResolveAsync(command.Members ?? [], null, services, tests, cancellationToken);

        var definition = ServiceMembers.Definition(
            command.ShortName, command.ShortcutNo, command.NameEn, command.NameAr, command.ReportName, command.Loinc, command.Cpt, command.ClinicalGroupId,
            command.SampleTypeId, command.SampleConditionId, command.Header, command.Aliases, command.Precaution, command.Gender, command.TurnaroundHours,
            command.LabelCount, command.PrintAlone, command.IsOrderable, command.ShowOnWeb, command.ProfileSum);
        var sequence = await services.NextSequenceAsync(cancellationToken);
        var service = Service.Create(code, Enum.Parse<ServiceType>(command.Type, ignoreCase: true), definition, sequence, members, clock.UtcNow);

        services.Add(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(service.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateServiceCommand(
    Guid ServiceId,
    string? Type,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid? ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    string? Header,
    IReadOnlyList<string>? Aliases,
    string? Precaution,
    string Gender,
    int TurnaroundHours,
    int LabelCount,
    bool PrintAlone,
    bool IsOrderable,
    bool ShowOnWeb,
    decimal? ProfileSum) : ICommand;

public sealed class UpdateServiceValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceValidator()
    {
        RuleFor(command => command.ServiceId).NotEmpty();
        RuleFor(command => command.Type).IsEnumName(typeof(ServiceType), caseSensitive: false).When(command => !string.IsNullOrEmpty(command.Type));
        RuleFor(command => command.ShortName).NotEmpty().MaximumLength(Service.MaxShortNameLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.ReportName).MaximumLength(Service.MaxReportNameLength);
        RuleFor(command => command.Loinc).MaximumLength(Service.MaxReferenceCodeLength);
        RuleFor(command => command.Cpt).MaximumLength(Service.MaxReferenceCodeLength);
        RuleFor(command => command.Header).MaximumLength(Service.MaxHeaderLength);
        RuleFor(command => command.Precaution).MaximumLength(Service.MaxPrecautionLength);
        RuleFor(command => command.Gender).NotEmpty().IsEnumName(typeof(GenderRestriction), caseSensitive: false);
        RuleFor(command => command.Aliases!.Count).LessThanOrEqualTo(Service.MaxAliases).When(command => command.Aliases is not null);
        RuleForEach(command => command.Aliases).NotEmpty().MaximumLength(Service.MaxAliasLength);
    }
}

public sealed class UpdateServiceHandler(
    IServiceRepository services,
    IClinicalGroupRepository groups,
    ISampleTypeRepository sampleTypes,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateServiceCommand>
{
    public async Task Handle(UpdateServiceCommand command, CancellationToken cancellationToken)
    {
        var service = await services.GetForUpdateAsync(command.ServiceId, cancellationToken)
            ?? throw new DomainException(ServiceCodes.NotFound, "Service not found.", DomainFault.NotFound);

        if (!string.IsNullOrEmpty(command.Type) && Enum.Parse<ServiceType>(command.Type, ignoreCase: true) != service.Type)
        {
            throw new DomainException(ServiceCodes.TypeImmutable, "The type of a service cannot change; create a new service.", DomainFault.Conflict);
        }

        if (command.ShortcutNo is { } shortcut && shortcut != service.ShortcutNo && await services.ShortcutTakenAsync(shortcut, service.Id, cancellationToken))
        {
            throw new DomainException(ServiceCodes.ShortcutTaken, $"The shortcut number {shortcut} is used by another service.", DomainFault.Conflict);
        }

        await CatalogueAReferences.RequireClinicalGroupAsync(groups, command.ClinicalGroupId, service.ClinicalGroupId, cancellationToken);
        await CatalogueAReferences.RequireSampleAsync(sampleTypes, command.SampleTypeId, command.SampleConditionId, service.SampleTypeId, service.SampleConditionId, cancellationToken);

        var definition = ServiceMembers.Definition(
            command.ShortName, command.ShortcutNo, command.NameEn, command.NameAr, command.ReportName, command.Loinc, command.Cpt, command.ClinicalGroupId,
            command.SampleTypeId, command.SampleConditionId, command.Header, command.Aliases, command.Precaution, command.Gender, command.TurnaroundHours,
            command.LabelCount, command.PrintAlone, command.IsOrderable, command.ShowOnWeb, command.ProfileSum);
        service.Change(definition, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record ReplaceServiceMembersCommand(Guid ServiceId, IReadOnlyList<ServiceMemberItem> Members) : ICommand;

public sealed class ReplaceServiceMembersValidator : AbstractValidator<ReplaceServiceMembersCommand>
{
    public ReplaceServiceMembersValidator()
    {
        RuleFor(command => command.ServiceId).NotEmpty();
        RuleFor(command => command.Members).NotNull();
        RuleFor(command => command.Members.Count).LessThanOrEqualTo(Service.MaxMembers).When(command => command.Members is not null);
        RuleForEach(command => command.Members).SetValidator(new ServiceMemberItemValidator());
    }
}

public sealed class ReplaceServiceMembersHandler(IServiceRepository services, ILabTestRepository tests, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReplaceServiceMembersCommand>
{
    public async Task Handle(ReplaceServiceMembersCommand command, CancellationToken cancellationToken)
    {
        var service = await services.GetForUpdateAsync(command.ServiceId, cancellationToken)
            ?? throw new DomainException(ServiceCodes.NotFound, "Service not found.", DomainFault.NotFound);

        var members = await ServiceMembers.ResolveAsync(command.Members, service, services, tests, cancellationToken);
        service.ReplaceMembers(members, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// T12 (CP-42): the sequences of the listed services; INV-CAT-004 holds over the whole tenant after the change (a swap in one save is
/// fine). The reorder page sends only the rows it shows, renumbered within the numbers those rows already occupied, so a filtered subset
/// never collides with the rows outside it; a number sent twice is CAT-033, a number held by a row outside the list CAT-105.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record SaveServiceSequenceCommand(IReadOnlyList<ServiceSequenceChange> Items) : ICommand;

public sealed class SaveServiceSequenceValidator : AbstractValidator<SaveServiceSequenceCommand>
{
    public const int MaxItems = 5000;

    public SaveServiceSequenceValidator()
    {
        RuleFor(command => command.Items).NotEmpty();
        RuleFor(command => command.Items.Count).LessThanOrEqualTo(MaxItems).When(command => command.Items is not null);
        RuleFor(command => command.Items).Must(items => items.Select(item => item.ServiceId).Distinct().Count() == items.Count)
            .When(command => command.Items is not null)
            .WithMessage("A service is listed twice.");
        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(change => change.ServiceId).NotEmpty();
            item.RuleFor(change => change.Sequence).GreaterThan(0);
        });
    }
}

public sealed class SaveServiceSequenceHandler(IServiceRepository services, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SaveServiceSequenceCommand>
{
    public async Task Handle(SaveServiceSequenceCommand command, CancellationToken cancellationToken)
    {
        Service.EnsureDistinctSequences(command.Items.Select(item => item.Sequence));   // CAT-033 before any load

        var ids = command.Items.Select(item => item.ServiceId).ToList();
        var loaded = await services.GetManyForUpdateAsync(ids, cancellationToken);
        if (loaded.Count != ids.Count)
        {
            throw new DomainException(ServiceCodes.NotFound, "A listed service was not found.", DomainFault.NotFound);
        }

        var final = new Dictionary<Guid, int>(await services.SequencesAsync(cancellationToken));
        foreach (var item in command.Items)
        {
            final[item.ServiceId] = item.Sequence;
        }

        var duplicate = final.GroupBy(pair => pair.Value).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException(ServiceCodes.SequenceTaken, $"The sequence {duplicate.Key} would be used by more than one service.", DomainFault.Conflict);
        }

        var now = clock.UtcNow;
        var byId = loaded.ToDictionary(service => service.Id);
        foreach (var item in command.Items)
        {
            byId[item.ServiceId].Resequence(item.Sequence, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Manage)]
public sealed record SetServiceActiveCommand(Guid ServiceId, bool Active) : ICommand;

public sealed class SetServiceActiveValidator : AbstractValidator<SetServiceActiveCommand>
{
    public SetServiceActiveValidator() => RuleFor(command => command.ServiceId).NotEmpty();
}

public sealed class SetServiceActiveHandler(IServiceRepository services, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetServiceActiveCommand>
{
    public async Task Handle(SetServiceActiveCommand command, CancellationToken cancellationToken)
    {
        var service = await services.GetForUpdateAsync(command.ServiceId, cancellationToken)
            ?? throw new DomainException(ServiceCodes.NotFound, "Service not found.", DomainFault.NotFound);

        if (command.Active)
        {
            service.Reactivate(clock.UtcNow);
        }
        else
        {
            service.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>What the service handlers share: the definition record and the member resolution (CAT-027).</summary>
internal static class ServiceMembers
{
    public static ServiceDefinition Definition(
        string shortName,
        int? shortcutNo,
        string nameEn,
        string nameAr,
        string? reportName,
        string? loinc,
        string? cpt,
        Guid? clinicalGroupId,
        Guid? sampleTypeId,
        Guid? sampleConditionId,
        string? header,
        IReadOnlyList<string>? aliases,
        string? precaution,
        string gender,
        int turnaroundHours,
        int labelCount,
        bool printAlone,
        bool isOrderable,
        bool showOnWeb,
        decimal? profileSum) =>
        new(
            shortName,
            shortcutNo,
            new LocalizedText(nameEn, nameAr),
            reportName,
            loinc,
            cpt,
            clinicalGroupId,
            sampleTypeId,
            sampleConditionId,
            header,
            aliases ?? [],
            precaution,
            Enum.Parse<GenderRestriction>(gender, ignoreCase: true),
            turnaroundHours,
            labelCount,
            printAlone,
            isOrderable,
            showOnWeb,
            profileSum);

    /// <summary>Each member exists in the tenant and, when newly selected, is active (CAT-027); its type travels to the aggregate.</summary>
    public static async Task<IReadOnlyList<ResolvedServiceMember>> ResolveAsync(
        IReadOnlyList<ServiceMemberItem> items,
        Service? current,
        IServiceRepository services,
        ILabTestRepository tests,
        CancellationToken cancellationToken)
    {
        var parsed = items.Select(item => (Kind: Enum.Parse<ServiceMemberKind>(item.Kind, ignoreCase: true), Item: item)).ToList();
        var testIds = parsed.Where(member => member.Kind == ServiceMemberKind.Test).Select(member => member.Item.MemberId).Distinct().ToList();
        var serviceIds = parsed.Where(member => member.Kind == ServiceMemberKind.Service).Select(member => member.Item.MemberId).Distinct().ToList();
        var testStates = testIds.Count == 0 ? new Dictionary<Guid, LabTestMemberState>() : await tests.MemberStatesAsync(testIds, cancellationToken);
        var serviceStates = serviceIds.Count == 0 ? new Dictionary<Guid, ServiceMemberState>() : await services.MemberStatesAsync(serviceIds, cancellationToken);

        var resolved = new List<ResolvedServiceMember>(parsed.Count);
        foreach (var (kind, item) in parsed)
        {
            var kept = current?.HasMember(kind, item.MemberId) ?? false;
            if (kind == ServiceMemberKind.Test)
            {
                if (!testStates.TryGetValue(item.MemberId, out var test) || (!test.IsActive && !kept))
                {
                    throw Unavailable("test");
                }

                resolved.Add(new ResolvedServiceMember(kind, item.MemberId, item.Order, item.InSum, item.Visible, test.Type, null));
            }
            else
            {
                if (!serviceStates.TryGetValue(item.MemberId, out var member) || (!member.IsActive && !kept && member.Id != current?.Id))
                {
                    throw Unavailable("service");
                }

                resolved.Add(new ResolvedServiceMember(kind, item.MemberId, item.Order, item.InSum, item.Visible, null, member.Type));
            }
        }

        return resolved;
    }

    private static DomainException Unavailable(string what) =>
        new(ServiceCodes.MemberUnavailable, $"A member {what} does not exist or is inactive (BR-24).", DomainFault.Validation);
}
