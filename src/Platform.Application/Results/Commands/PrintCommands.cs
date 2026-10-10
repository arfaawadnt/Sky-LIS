using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Organization;
using Platform.Contracts.Common;
using Platform.Contracts.Results;
using Platform.Domain.Authorization;
using Platform.Domain.Results;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Results.Commands;

// T8 Print a report unit (CP-19 "Print"; LDM §5.7).

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Export)]
public sealed record PrintReportUnitCommand(string ReportUnitKey) : ICommand;

public sealed class PrintReportUnitValidator : AbstractValidator<PrintReportUnitCommand>
{
    public PrintReportUnitValidator() => RuleFor(command => command.ReportUnitKey).Must(ReportUnits.IsWellFormed).WithMessage("A report unit key is required.");
}

/// <summary>Every live result of the unit is printable first (nothing is marked when one refuses), then all are marked printed.</summary>
public sealed class PrintReportUnitHandler(IResultRepository results, ITenantSettings settings, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<PrintReportUnitCommand>
{
    public async Task Handle(PrintReportUnitCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var printUnverified = await settings.GetAsync(SettingKeys.PrintUnverified, cancellationToken);
        var unit = await results.ListByUnitsForUpdateAsync([command.ReportUnitKey], cancellationToken);
        UnitPrinting.Print(unit, ResultRights.VisibilityOf(privileges), result => ResultRights.Actor(actor, name, privileges, result), printUnverified, null, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T8 over a report unit: visible (RES-405 otherwise), every live result printable, then all marked.</summary>
internal static class UnitPrinting
{
    public static void EnsurePrintable(IReadOnlyList<Result> unit, ResultVisibility visibility, Func<Result, ResultActor> actorOf, bool printUnverified)
    {
        var live = unit.Where(result => result.IsLive).ToList();
        if (live.Count == 0 || !live.Any(result => visibility.CanSee(result.ClinicalGroupId, result.BranchId)))
        {
            throw ResultsNotFound.Unit();
        }

        foreach (var result in live)
        {
            result.EnsurePrintable(actorOf(result), printUnverified);
        }
    }

    public static void Print(IReadOnlyList<Result> unit, ResultVisibility visibility, Func<Result, ResultActor> actorOf, bool printUnverified, string? printListNumber, DateTimeOffset nowUtc)
    {
        EnsurePrintable(unit, visibility, actorOf, printUnverified);
        foreach (var result in unit.Where(result => result.IsLive))
        {
            result.MarkPrinted(actorOf(result), printUnverified, printListNumber, nowUtc);
        }
    }
}
