using MediatR;
using Microsoft.AspNetCore.Mvc;
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
