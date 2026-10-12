using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Treasury.Commands;
using Platform.Application.Treasury.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Treasury;

namespace Platform.WebApi.Controllers.Treasury;

/// <summary>Treasury master (SRS CP-28). Dispatch only: the policy on each request decides who may call it (D-04).</summary>
[ApiController]
[Route("api/v1/treasuries")]
public sealed class TreasuriesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<TreasuryListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListTreasuriesQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateTreasuryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateTreasuryCommand(request.Code, request.NameEn, request.NameAr, request.BranchId), cancellationToken);
        return Created($"/api/v1/treasuries/{created.Id}", created);
    }

    [HttpPut("{id:guid}/name")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameTreasuryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new RenameTreasuryCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateTreasuryCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateTreasuryCommand(id), cancellationToken);
        return NoContent();
    }
}
