using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Integration.Commands;
using Platform.Application.Integration.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;

namespace Platform.WebApi.Controllers.Integration;

/// <summary>PCR kits (SRS CP-62) and the run simulation. Dispatch only.</summary>
[ApiController]
[Route("api/v1/integration/pcr-kits")]
public sealed class PcrKitsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PcrKitListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? analyserId, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPcrKitsQuery(search, isActive, analyserId, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PcrKitDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPcrKitQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] SavePcrKitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new DefinePcrKitCommand(request), cancellationToken);
        return Created($"/api/v1/integration/pcr-kits/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavePcrKitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdatePcrKitCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivatePcrKitCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivatePcrKitCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Reads a pasted run file with the kit; nothing is stored or raised.</summary>
    [HttpPost("{id:guid}/simulate")]
    public async Task<ActionResult<PcrRunView>> Simulate(Guid id, [FromBody] SimulatePcrRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new SimulatePcrRunQuery(id, request.InstrumentSerial, request.Content), cancellationToken));
    }
}
