using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Catalogue;

/// <summary>Services (CP-40), the service sequence (CP-42) and additional services (CP-55, <c>?type=Additional</c>).</summary>
[ApiController]
[Route("api/v1/catalogue/services")]
public sealed class ServicesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<ServiceListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] Guid? clinicalGroupId,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListServicesQuery(search, type, clinicalGroupId, isActive, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<ServiceListItem>>> Export([FromQuery] string? search, [FromQuery] string? type, [FromQuery] Guid? clinicalGroupId, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportServicesQuery(search, type, clinicalGroupId, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServiceDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetServiceQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateServiceRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(
            new CreateServiceCommand(
                body.Code, body.Type, body.ShortName, body.ShortcutNo, body.NameEn, body.NameAr, body.ReportName, body.Loinc, body.Cpt, body.ClinicalGroupId,
                body.SampleTypeId, body.SampleConditionId, body.Header, body.Aliases, body.Precaution, body.Gender, body.TurnaroundHours, body.LabelCount,
                body.PrintAlone, body.IsOrderable, body.ShowOnWeb, body.ProfileSum, body.Members),
            cancellationToken);
        return Created($"/api/v1/catalogue/services/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(
            new UpdateServiceCommand(
                id, body.Type, body.ShortName, body.ShortcutNo, body.NameEn, body.NameAr, body.ReportName, body.Loinc, body.Cpt, body.ClinicalGroupId,
                body.SampleTypeId, body.SampleConditionId, body.Header, body.Aliases, body.Precaution, body.Gender, body.TurnaroundHours, body.LabelCount,
                body.PrintAlone, body.IsOrderable, body.ShowOnWeb, body.ProfileSum),
            cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/members")]
    public async Task<IActionResult> ReplaceMembers(Guid id, [FromBody] ReplaceServiceMembersRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new ReplaceServiceMembersCommand(id, body.Members), cancellationToken);
        return NoContent();
    }

    [HttpGet("sequence")]
    public async Task<ActionResult<IReadOnlyList<ServiceSequenceRow>>> Sequence([FromQuery] string? search, [FromQuery] string? type, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListServiceSequenceQuery(search, type), cancellationToken));

    [HttpPut("sequence")]
    public async Task<IActionResult> SaveSequence([FromBody] SaveServiceSequenceRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new SaveServiceSequenceCommand(body.Items), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetServiceActiveCommand(id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetServiceActiveCommand(id, true), cancellationToken);
        return NoContent();
    }
}

/// <summary>Pickers for every later module: active services and tests, ≤ 50 per page (catalogue.view).</summary>
[ApiController]
[Route("api/v1/catalogue/lookups")]
public sealed class ServiceTestLookupsController(ISender sender) : ControllerBase
{
    [HttpGet("services")]
    public async Task<ActionResult<Paged<ServiceLookupItem>>> Services(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] bool? orderable,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new LookupServicesQuery(search, type, orderable, page, pageSize), cancellationToken));

    [HttpGet("tests")]
    public async Task<ActionResult<Paged<TestLookupItem>>> Tests([FromQuery] string? search, [FromQuery] string? type, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new LookupTestsQuery(search, type, page, pageSize), cancellationToken));
}
