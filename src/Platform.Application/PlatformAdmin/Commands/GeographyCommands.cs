using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// Countries (SA-10) and governorates (SA-11): reference data of client provisioning (intake §7 M1–M3).

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record CreateCountryCommand(string NameEn, string NameAr, string IsoCode, string DialCode, string Currency, string CurrencyCode, string Language, string TimeZone, int MobileLength, string? Flag, string? Remarks)
    : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreateCountryValidator : AbstractValidator<CreateCountryCommand>
{
    public CreateCountryValidator()
    {
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Country.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Country.MaxNameLength);
        RuleFor(command => command.IsoCode).NotEmpty().Length(2);
        RuleFor(command => command.DialCode).NotEmpty().MaximumLength(6);
        RuleFor(command => command.Currency).NotEmpty().MaximumLength(Country.MaxCurrencyNameLength);
        RuleFor(command => command.CurrencyCode).NotEmpty().Length(3);
        RuleFor(command => command.Language).NotEmpty().MaximumLength(2);
        RuleFor(command => command.TimeZone).NotEmpty().MaximumLength(Country.MaxTimeZoneLength);
        RuleFor(command => command.MobileLength).InclusiveBetween(Country.MinMobileLength, Country.MaxMobileLength);
        RuleFor(command => command.Flag).MaximumLength(Country.MaxFlagLength);
        RuleFor(command => command.Remarks).MaximumLength(Country.MaxRemarksLength);
    }
}

public sealed class CreateCountryHandler(ICountryRepository countries, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<CreateCountryCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateCountryCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var country = Country.Create(new CountryData(command.NameEn, command.NameAr, command.IsoCode, command.DialCode, command.Currency, command.CurrencyCode, command.Language, command.TimeZone, command.MobileLength, command.Flag, command.Remarks));
        if (await countries.IsoCodeExistsAsync(country.IsoCode, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.IsoCodeExists, $"A country with ISO code '{country.IsoCode}' already exists.", DomainFault.Conflict);
        }

        countries.Add(country);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(country.Id);
    }
}

/// <summary>Edits a country; the ISO code (natural key) is kept.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record UpdateCountryCommand(Guid CountryId, string NameEn, string NameAr, string DialCode, string Currency, string CurrencyCode, string Language, string TimeZone, int MobileLength, string? Flag, string? Remarks)
    : ICommand, IPlatformAdministrationRequest;

public sealed class UpdateCountryValidator : AbstractValidator<UpdateCountryCommand>
{
    public UpdateCountryValidator()
    {
        RuleFor(command => command.CountryId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Country.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Country.MaxNameLength);
        RuleFor(command => command.DialCode).NotEmpty().MaximumLength(6);
        RuleFor(command => command.Currency).NotEmpty().MaximumLength(Country.MaxCurrencyNameLength);
        RuleFor(command => command.CurrencyCode).NotEmpty().Length(3);
        RuleFor(command => command.Language).NotEmpty().MaximumLength(2);
        RuleFor(command => command.TimeZone).NotEmpty().MaximumLength(Country.MaxTimeZoneLength);
        RuleFor(command => command.MobileLength).InclusiveBetween(Country.MinMobileLength, Country.MaxMobileLength);
        RuleFor(command => command.Flag).MaximumLength(Country.MaxFlagLength);
        RuleFor(command => command.Remarks).MaximumLength(Country.MaxRemarksLength);
    }
}

public sealed class UpdateCountryHandler(ICountryRepository countries, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<UpdateCountryCommand>
{
    public async Task Handle(UpdateCountryCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var country = await countries.GetForUpdateAsync(command.CountryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        country.Update(new CountryData(command.NameEn, command.NameAr, country.IsoCode, command.DialCode, command.Currency, command.CurrencyCode, command.Language, command.TimeZone, command.MobileLength, command.Flag, command.Remarks));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record SetCountryActiveCommand(Guid CountryId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetCountryActiveValidator : AbstractValidator<SetCountryActiveCommand>
{
    public SetCountryActiveValidator() => RuleFor(command => command.CountryId).NotEmpty();
}

public sealed class SetCountryActiveHandler(ICountryRepository countries, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SetCountryActiveCommand>
{
    public async Task Handle(SetCountryActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var country = await countries.GetForUpdateAsync(command.CountryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        if (command.Active)
        {
            country.Reactivate();
        }
        else
        {
            country.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>M3 Delete a country that nothing references (INV-PLT-010, GEO-110); otherwise deactivate it.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record DeleteCountryCommand(Guid CountryId) : ICommand, IPlatformAdministrationRequest;

public sealed class DeleteCountryValidator : AbstractValidator<DeleteCountryCommand>
{
    public DeleteCountryValidator() => RuleFor(command => command.CountryId).NotEmpty();
}

public sealed class DeleteCountryHandler(ICountryRepository countries, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<DeleteCountryCommand>
{
    public async Task Handle(DeleteCountryCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var country = await countries.GetForUpdateAsync(command.CountryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        if (await countries.IsReferencedAsync(country.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.CountryInUse, "The country has governorates or clients; deactivate it instead.", DomainFault.Conflict);
        }

        countries.Remove(country);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record CreateGovernorateCommand(Guid CountryId, string NameEn, string NameAr, string? District, string? Remarks) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreateGovernorateValidator : AbstractValidator<CreateGovernorateCommand>
{
    public CreateGovernorateValidator()
    {
        RuleFor(command => command.CountryId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Governorate.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Governorate.MaxNameLength);
        RuleFor(command => command.District).MaximumLength(Governorate.MaxDistrictLength);
        RuleFor(command => command.Remarks).MaximumLength(Governorate.MaxRemarksLength);
    }
}

public sealed class CreateGovernorateHandler(ICountryRepository countries, IGovernorateRepository governorates, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<CreateGovernorateCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateGovernorateCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var country = await countries.GetForUpdateAsync(command.CountryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        var governorate = Governorate.Create(country, command.NameEn, command.NameAr, command.District, command.Remarks);
        if (await governorates.NameExistsAsync(country.Id, governorate.Name.En, null, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.GovernorateExists, $"'{governorate.Name.En}' already exists in this country.", DomainFault.Conflict);
        }

        governorates.Add(governorate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(governorate.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record UpdateGovernorateCommand(Guid GovernorateId, string NameEn, string NameAr, string? District, string? Remarks) : ICommand, IPlatformAdministrationRequest;

public sealed class UpdateGovernorateValidator : AbstractValidator<UpdateGovernorateCommand>
{
    public UpdateGovernorateValidator()
    {
        RuleFor(command => command.GovernorateId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Governorate.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Governorate.MaxNameLength);
        RuleFor(command => command.District).MaximumLength(Governorate.MaxDistrictLength);
        RuleFor(command => command.Remarks).MaximumLength(Governorate.MaxRemarksLength);
    }
}

public sealed class UpdateGovernorateHandler(IGovernorateRepository governorates, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<UpdateGovernorateCommand>
{
    public async Task Handle(UpdateGovernorateCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var governorate = await governorates.GetForUpdateAsync(command.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        governorate.Update(command.NameEn, command.NameAr, command.District, command.Remarks);
        if (await governorates.NameExistsAsync(governorate.CountryId, governorate.Name.En, governorate.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.GovernorateExists, $"'{governorate.Name.En}' already exists in this country.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record SetGovernorateActiveCommand(Guid GovernorateId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetGovernorateActiveValidator : AbstractValidator<SetGovernorateActiveCommand>
{
    public SetGovernorateActiveValidator() => RuleFor(command => command.GovernorateId).NotEmpty();
}

public sealed class SetGovernorateActiveHandler(IGovernorateRepository governorates, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SetGovernorateActiveCommand>
{
    public async Task Handle(SetGovernorateActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var governorate = await governorates.GetForUpdateAsync(command.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        if (command.Active)
        {
            governorate.Reactivate();
        }
        else
        {
            governorate.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
