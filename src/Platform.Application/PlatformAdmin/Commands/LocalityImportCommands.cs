using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

/// <summary>
/// LIS-CR-008 C-04 (decision D-2), SA-11 "Import CSV": the cities, districts and areas of one governorate from a file
/// (<see cref="LocalityCsv"/>) [tenancy.manage, like every write of the Locations drawer]. Every line is planned
/// (<see cref="LocalityImport.Plan"/>: matched case-insensitively per parent, idempotent); with any refused line, or with
/// <paramref name="DryRun"/>, nothing is written (all-or-nothing); otherwise the new rows are added in one save. The answer
/// counts created / existing / refused per level and lists the refused lines.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record ImportLocalitiesCommand(Guid GovernorateId, Stream Content, long Length, bool DryRun) : ICommand<LocalityImportResult>, IPlatformAdministrationRequest;

public sealed class ImportLocalitiesValidator : AbstractValidator<ImportLocalitiesCommand>
{
    public ImportLocalitiesValidator()
    {
        RuleFor(command => command.GovernorateId).NotEmpty();
        RuleFor(command => command.Content).NotNull();
        RuleFor(command => command.Length).GreaterThan(0);
    }
}

public sealed class ImportLocalitiesHandler(
    IGovernorateRepository governorates,
    ICityRepository cities,
    IDistrictRepository districts,
    IAreaRepository areas,
    ICurrentTenant currentTenant,
    IUnitOfWork unitOfWork) : ICommandHandler<ImportLocalitiesCommand, LocalityImportResult>
{
    public async Task<LocalityImportResult> Handle(ImportLocalitiesCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        if (command.Length > LocalityCsv.MaxBytes)
        {
            throw new DomainException(PlatformAdminCodes.LocalityImportTooLarge, $"The file exceeds {LocalityCsv.MaxBytes / (1024 * 1024)} MB.", DomainFault.Validation);
        }

        var governorate = await governorates.GetForUpdateAsync(command.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        var rows = await LocalityCsv.ReadAsync(command.Content, cancellationToken);

        var storedCities = await cities.ListForImportAsync(governorate.Id, cancellationToken);
        var storedDistricts = await districts.ListForImportAsync(storedCities.Select(city => city.Id).ToList(), cancellationToken);
        var storedAreas = await areas.ListForImportAsync(storedDistricts.Select(district => district.Id).ToList(), cancellationToken);
        var plan = LocalityImport.Plan(governorate, storedCities, storedDistricts, storedAreas, rows);

        var apply = plan.IsClean && !command.DryRun;
        if (apply)
        {
            foreach (var city in plan.NewCities)
            {
                cities.Add(city);
            }

            foreach (var district in plan.NewDistricts)
            {
                districts.Add(district);
            }

            foreach (var area in plan.NewAreas)
            {
                areas.Add(area);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new LocalityImportResult(
            rows.Count,
            Level(plan.Cities),
            Level(plan.Districts),
            Level(plan.Areas),
            command.DryRun,
            apply,
            plan.Refusals.Select(refusal => new LocalityImportError(refusal.Line, refusal.Level.ToString(), refusal.Code, refusal.Message)).ToList());
    }

    private static LocalityImportLevel Level(LocalityImportCount count) => new(count.Created, count.Existing, count.Refused);
}
