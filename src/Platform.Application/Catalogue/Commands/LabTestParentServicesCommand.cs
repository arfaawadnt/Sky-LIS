using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Catalogue;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Commands;

/// <summary>
/// T21 (CP-41 "parent services", change request 2026-10-14): the services that carry the test, set from the test page.
/// The test joins every listed service it is not yet a member of (last in order, printed, not in the sum) and leaves
/// every carrying service no longer listed. Each change is a <see cref="Service.ReplaceMembers"/> of that service, so
/// the member rules of its type decide: a test service or a text report keeps its single test (CAT-021), a package
/// takes no test (CAT-022), a culture keeps exactly one culture test (CAT-036), an inactive test joins nothing
/// (CAT-027). One refusal leaves every service unchanged.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record SetLabTestParentServicesCommand(Guid TestId, IReadOnlyList<Guid> ServiceIds) : ICommand;

public sealed class SetLabTestParentServicesValidator : AbstractValidator<SetLabTestParentServicesCommand>
{
    /// <summary>A test is carried by at most this many services in one call (a tenant's catalogue is far smaller).</summary>
    public const int MaxServiceIds = 500;

    public SetLabTestParentServicesValidator()
    {
        RuleFor(command => command.TestId).NotEmpty();
        RuleFor(command => command.ServiceIds).NotNull();
        RuleFor(command => command.ServiceIds).Must(ids => ids.Count <= MaxServiceIds).When(command => command.ServiceIds is not null).WithMessage($"At most {MaxServiceIds} services.");
        RuleFor(command => command.ServiceIds).Must(ids => ids.Distinct().Count() == ids.Count).When(command => command.ServiceIds is not null).WithMessage("A service is listed twice.");
        RuleForEach(command => command.ServiceIds).NotEmpty();
    }
}

public sealed class SetLabTestParentServicesHandler(ILabTestRepository tests, IServiceRepository services, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetLabTestParentServicesCommand>
{
    public async Task Handle(SetLabTestParentServicesCommand command, CancellationToken cancellationToken)
    {
        var known = await tests.MemberStatesAsync([command.TestId], cancellationToken);
        if (!known.ContainsKey(command.TestId))
        {
            throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);
        }

        var wanted = command.ServiceIds.ToHashSet();
        var carrying = await services.GetCarryingTestForUpdateAsync(command.TestId, cancellationToken);
        var carryingIds = carrying.Select(service => service.Id).ToHashSet();
        var joiningIds = wanted.Where(id => !carryingIds.Contains(id)).ToList();
        IReadOnlyList<Service> joining = joiningIds.Count == 0 ? [] : await services.GetManyForUpdateAsync(joiningIds, cancellationToken);
        if (joining.Count != joiningIds.Count)
        {
            throw new DomainException(ServiceCodes.NotFound, "Service not found.", DomainFault.NotFound);
        }

        var now = clock.UtcNow;
        foreach (var service in carrying.Where(service => !wanted.Contains(service.Id)))
        {
            var remaining = Items(service.Members.Where(member => !(member.Kind == ServiceMemberKind.Test && member.MemberId == command.TestId)));
            service.ReplaceMembers(await ServiceMembers.ResolveAsync(remaining, service, services, tests, cancellationToken), now);
        }

        foreach (var service in joining)
        {
            var next = service.Members.Count == 0 ? 1 : service.Members.Max(member => member.Order) + 1;
            var items = Items(service.Members);
            items.Add(new ServiceMemberItem(nameof(ServiceMemberKind.Test), command.TestId, next, false, true));
            service.ReplaceMembers(await ServiceMembers.ResolveAsync(items, service, services, tests, cancellationToken), now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static List<ServiceMemberItem> Items(IEnumerable<ServiceMember> members) =>
        members.Select(member => new ServiceMemberItem(member.Kind.ToString(), member.MemberId, member.Order, member.InSum, member.Visible)).ToList();
}
