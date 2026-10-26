using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Catalogue;

/// <summary>Compatibility groups of samples (change request LIS-CR-015, note M4). Dispatch only: the policy on each request decides who may call it.</summary>
[ApiController]
[Route("api/v1/catalogue/sample-compatibility-groups")]
public sealed class SampleCompatibilityGroupsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SampleCompatibilityGroupView>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSampleCompatibilityGroupsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<SampleCompatibilityGroupView>>> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportSampleCompatibilityGroupsQuery(search, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SampleCompatibilityGroupView>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSampleCompatibilityGroupQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateSampleCompatibilityGroupRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(new CreateSampleCompatibilityGroupCommand(body.Code, body.NameEn, body.NameAr, body.Members), cancellationToken);
        return Created($"/api/v1/catalogue/sample-compatibility-groups/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSampleCompatibilityGroupRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new UpdateSampleCompatibilityGroupCommand(id, body.NameEn, body.NameAr, body.Members), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetSampleCompatibilityGroupActiveCommand(id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetSampleCompatibilityGroupActiveCommand(id, true), cancellationToken);
        return NoContent();
    }
}
