using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// The location hierarchy under a governorate (change request 2026-10-19, A2 — SA-11b "Locations"): cities, districts
// and areas are created, renamed, deactivated and reactivated from the Governorates page [tenancy.manage]. Duplicate
// English names within a parent are refused before the aggregate (GEO-102/103/104); the unique index backs them.

// ── Cities ───────────────────────────────────────────────────────────────────────────────────────

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record CreateCityCommand(Guid GovernorateId, string NameEn, string NameAr) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreateCityValidator : AbstractValidator<CreateCityCommand>
{
    public CreateCityValidator()
    {
        RuleFor(command => command.GovernorateId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
    }
}

public sealed class CreateCityHandler(IGovernorateRepository governorates, ICityRepository cities, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<CreateCityCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateCityCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var governorate = await governorates.GetForUpdateAsync(command.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        var city = City.Create(governorate, command.NameEn, command.NameAr);
        if (await cities.NameExistsAsync(governorate.Id, city.Name.En, null, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.CityExists, $"'{city.Name.En}' already exists in this governorate.", DomainFault.Conflict);
        }

        cities.Add(city);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(city.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record RenameCityCommand(Guid CityId, string NameEn, string NameAr) : ICommand, IPlatformAdministrationRequest;

public sealed class RenameCityValidator : AbstractValidator<RenameCityCommand>
{
    public RenameCityValidator()
    {
        RuleFor(command => command.CityId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
    }
}

public sealed class RenameCityHandler(ICityRepository cities, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<RenameCityCommand>
{
    public async Task Handle(RenameCityCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var city = await cities.GetForUpdateAsync(command.CityId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CityNotFound, "City not found.", DomainFault.NotFound);
        city.Rename(command.NameEn, command.NameAr);
        if (await cities.NameExistsAsync(city.GovernorateId, city.Name.En, city.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.CityExists, $"'{city.Name.En}' already exists in this governorate.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record SetCityActiveCommand(Guid CityId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetCityActiveValidator : AbstractValidator<SetCityActiveCommand>
{
    public SetCityActiveValidator() => RuleFor(command => command.CityId).NotEmpty();
}

public sealed class SetCityActiveHandler(ICityRepository cities, IGovernorateRepository governorates, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SetCityActiveCommand>
{
    public async Task Handle(SetCityActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var city = await cities.GetForUpdateAsync(command.CityId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CityNotFound, "City not found.", DomainFault.NotFound);
        if (command.Active)
        {
            var governorate = await governorates.GetForUpdateAsync(city.GovernorateId, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
            city.Reactivate(governorate);
        }
        else
        {
            city.Deactivate(await cities.HasActiveDistrictsAsync(city.Id, cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ── Districts ────────────────────────────────────────────────────────────────────────────────────

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record CreateDistrictCommand(Guid CityId, string NameEn, string NameAr) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreateDistrictValidator : AbstractValidator<CreateDistrictCommand>
{
    public CreateDistrictValidator()
    {
        RuleFor(command => command.CityId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
    }
}

public sealed class CreateDistrictHandler(ICityRepository cities, IDistrictRepository districts, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<CreateDistrictCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateDistrictCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var city = await cities.GetForUpdateAsync(command.CityId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CityNotFound, "City not found.", DomainFault.NotFound);
        var district = District.Create(city, command.NameEn, command.NameAr);
        if (await districts.NameExistsAsync(city.Id, district.Name.En, null, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.DistrictExists, $"'{district.Name.En}' already exists in this city.", DomainFault.Conflict);
        }

        districts.Add(district);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(district.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record RenameDistrictCommand(Guid DistrictId, string NameEn, string NameAr) : ICommand, IPlatformAdministrationRequest;

public sealed class RenameDistrictValidator : AbstractValidator<RenameDistrictCommand>
{
    public RenameDistrictValidator()
    {
        RuleFor(command => command.DistrictId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
    }
}

public sealed class RenameDistrictHandler(IDistrictRepository districts, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<RenameDistrictCommand>
{
    public async Task Handle(RenameDistrictCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var district = await districts.GetForUpdateAsync(command.DistrictId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.DistrictNotFound, "District not found.", DomainFault.NotFound);
        district.Rename(command.NameEn, command.NameAr);
        if (await districts.NameExistsAsync(district.CityId, district.Name.En, district.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.DistrictExists, $"'{district.Name.En}' already exists in this city.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record SetDistrictActiveCommand(Guid DistrictId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetDistrictActiveValidator : AbstractValidator<SetDistrictActiveCommand>
{
    public SetDistrictActiveValidator() => RuleFor(command => command.DistrictId).NotEmpty();
}

public sealed class SetDistrictActiveHandler(IDistrictRepository districts, ICityRepository cities, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SetDistrictActiveCommand>
{
    public async Task Handle(SetDistrictActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var district = await districts.GetForUpdateAsync(command.DistrictId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.DistrictNotFound, "District not found.", DomainFault.NotFound);
        if (command.Active)
        {
            var city = await cities.GetForUpdateAsync(district.CityId, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.CityNotFound, "City not found.", DomainFault.NotFound);
            district.Reactivate(city);
        }
        else
        {
            district.Deactivate(await districts.HasActiveAreasAsync(district.Id, cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ── Areas ────────────────────────────────────────────────────────────────────────────────────────

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record CreateAreaCommand(Guid DistrictId, string NameEn, string NameAr) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreateAreaValidator : AbstractValidator<CreateAreaCommand>
{
    public CreateAreaValidator()
    {
        RuleFor(command => command.DistrictId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
    }
}

public sealed class CreateAreaHandler(IDistrictRepository districts, IAreaRepository areas, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<CreateAreaCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateAreaCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var district = await districts.GetForUpdateAsync(command.DistrictId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.DistrictNotFound, "District not found.", DomainFault.NotFound);
        var area = Area.Create(district, command.NameEn, command.NameAr);
        if (await areas.NameExistsAsync(district.Id, area.Name.En, null, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.AreaExists, $"'{area.Name.En}' already exists in this district.", DomainFault.Conflict);
        }

        areas.Add(area);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(area.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record RenameAreaCommand(Guid AreaId, string NameEn, string NameAr) : ICommand, IPlatformAdministrationRequest;

public sealed class RenameAreaValidator : AbstractValidator<RenameAreaCommand>
{
    public RenameAreaValidator()
    {
        RuleFor(command => command.AreaId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocationRules.MaxNameLength);
    }
}

public sealed class RenameAreaHandler(IAreaRepository areas, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<RenameAreaCommand>
{
    public async Task Handle(RenameAreaCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var area = await areas.GetForUpdateAsync(command.AreaId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.AreaNotFound, "Area not found.", DomainFault.NotFound);
        area.Rename(command.NameEn, command.NameAr);
        if (await areas.NameExistsAsync(area.DistrictId, area.Name.En, area.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.AreaExists, $"'{area.Name.En}' already exists in this district.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record SetAreaActiveCommand(Guid AreaId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetAreaActiveValidator : AbstractValidator<SetAreaActiveCommand>
{
    public SetAreaActiveValidator() => RuleFor(command => command.AreaId).NotEmpty();
}

public sealed class SetAreaActiveHandler(IAreaRepository areas, IDistrictRepository districts, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SetAreaActiveCommand>
{
    public async Task Handle(SetAreaActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var area = await areas.GetForUpdateAsync(command.AreaId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.AreaNotFound, "Area not found.", DomainFault.NotFound);
        if (command.Active)
        {
            var district = await districts.GetForUpdateAsync(area.DistrictId, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.DistrictNotFound, "District not found.", DomainFault.NotFound);
            area.Reactivate(district);
        }
        else
        {
            area.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
