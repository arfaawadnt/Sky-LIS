using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>Countries (SRS SA-10).</summary>
[ApiController]
[Route("api/v1/platform/countries")]
public sealed class PlatformCountriesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<CountryListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCountriesQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CountryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateCountryCommand(request.NameEn, request.NameAr, request.IsoCode, request.DialCode, request.Currency, request.CurrencyCode, request.Language, request.TimeZone, request.MobileLength, request.Flag, request.Remarks), cancellationToken);
        return Created($"/api/v1/platform/countries/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CountryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateCountryCommand(id, request.NameEn, request.NameAr, request.DialCode, request.Currency, request.CurrencyCode, request.Language, request.TimeZone, request.MobileLength, request.Flag, request.Remarks), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCountryActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCountryActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }

    /// <summary>Refused with GEO-110 while a governorate or a client references the country.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteCountryCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Governorates (SRS SA-11).</summary>
[ApiController]
[Route("api/v1/platform/governorates")]
public sealed class PlatformGovernoratesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<GovernorateListItem>>> List([FromQuery] Guid? countryId, [FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListGovernoratesQuery(countryId, search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] GovernorateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateGovernorateCommand(request.CountryId, request.NameEn, request.NameAr, request.District, request.Remarks), cancellationToken);
        return Created($"/api/v1/platform/governorates/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] GovernorateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateGovernorateCommand(id, request.NameEn, request.NameAr, request.District, request.Remarks), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetGovernorateActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetGovernorateActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// The location hierarchy under a governorate (SRS SA-11b "Locations", change request 2026-10-19 A2): cities of a
/// governorate, districts of a city, areas of a district. Lists per parent [tenancy.view]; create, rename and the
/// activation pair [tenancy.manage]. Nothing is deleted.
/// </summary>
[ApiController]
[Route("api/v1/platform")]
public sealed class PlatformLocationsController(ISender sender) : ControllerBase
{
    [HttpGet("governorates/{id:guid}/cities")]
    public async Task<ActionResult<IReadOnlyList<LocationListItem>>> ListCities(Guid id, [FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCitiesQuery(id, search, isActive), cancellationToken));

    [HttpPost("governorates/{id:guid}/cities")]
    public async Task<ActionResult<CreatedResource>> CreateCity(Guid id, [FromBody] LocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateCityCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/platform/cities/{created.Id}", created);
    }

    /// <summary>The import body: the 4 MB file plus the multipart envelope.</summary>
    public const long LocalityImportRequestLimit = LocalityCsv.MaxBytes + (1024 * 1024);

    /// <summary>
    /// LIS-CR-008 C-04 (D-2) "Import CSV": multipart field <c>file</c> (UTF-8 CSV, header <c>city_en,city_ar,district_en,district_ar,area_en,area_ar</c>,
    /// district and area optional), <c>?dryRun=true</c> to check only. All-or-nothing and idempotent; the answer counts created /
    /// existing / refused rows per level and lists the refused lines (GEO-023 unreadable file, GEO-024 too large).
    /// </summary>
    [HttpPost("governorates/{id:guid}/localities/import")]
    [RequestSizeLimit(LocalityImportRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = LocalityImportRequestLimit)]
    public async Task<ActionResult<LocalityImportResult>> ImportLocalities(Guid id, IFormFile file, [FromQuery] bool dryRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var content = file.OpenReadStream();
        return Ok(await sender.Send(new ImportLocalitiesCommand(id, content, file.Length, dryRun), cancellationToken));
    }

    [HttpPut("cities/{id:guid}")]
    public async Task<IActionResult> RenameCity(Guid id, [FromBody] LocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new RenameCityCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("cities/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateCity(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCityActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    [HttpPost("cities/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateCity(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCityActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }

    [HttpGet("cities/{id:guid}/districts")]
    public async Task<ActionResult<IReadOnlyList<LocationListItem>>> ListDistricts(Guid id, [FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListDistrictsQuery(id, search, isActive), cancellationToken));

    [HttpPost("cities/{id:guid}/districts")]
    public async Task<ActionResult<CreatedResource>> CreateDistrict(Guid id, [FromBody] LocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateDistrictCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/platform/districts/{created.Id}", created);
    }

    [HttpPut("districts/{id:guid}")]
    public async Task<IActionResult> RenameDistrict(Guid id, [FromBody] LocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new RenameDistrictCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("districts/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateDistrict(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetDistrictActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    [HttpPost("districts/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateDistrict(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetDistrictActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }

    [HttpGet("districts/{id:guid}/areas")]
    public async Task<ActionResult<IReadOnlyList<LocationListItem>>> ListAreas(Guid id, [FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAreasQuery(id, search, isActive), cancellationToken));

    [HttpPost("districts/{id:guid}/areas")]
    public async Task<ActionResult<CreatedResource>> CreateArea(Guid id, [FromBody] LocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateAreaCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/platform/areas/{created.Id}", created);
    }

    [HttpPut("areas/{id:guid}")]
    public async Task<IActionResult> RenameArea(Guid id, [FromBody] LocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new RenameAreaCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("areas/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateArea(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetAreaActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    [HttpPost("areas/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateArea(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetAreaActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }
}

/// <summary>Notification templates (SRS SA-20).</summary>
[ApiController]
[Route("api/v1/platform/notification-templates")]
public sealed class PlatformNotificationTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<NotificationTemplateListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListNotificationTemplatesQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("placeholders")]
    public async Task<ActionResult<TemplatePlaceholders>> Placeholders(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTemplatePlaceholdersQuery(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] NotificationTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateNotificationTemplateCommand(request.Code, request.Channel, request.TextEn, request.TextAr, request.SubjectEn, request.SubjectAr), cancellationToken);
        return Created($"/api/v1/platform/notification-templates/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] NotificationTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateNotificationTemplateCommand(id, request.Channel, request.TextEn, request.TextAr, request.SubjectEn, request.SubjectAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetNotificationTemplateActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetNotificationTemplateActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }
}
