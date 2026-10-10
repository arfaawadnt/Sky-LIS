using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Platform.Application.Treasury.Commands;
using Platform.Application.Treasury.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Treasury;

namespace Platform.WebApi.Controllers.Treasury;

/// <summary>Treasury access requests (SRS CP-29): one endpoint per transition of the intake; reasons travel in typed bodies (pitfall P-16).</summary>
[ApiController]
[Route("api/v1/treasury-access-requests")]
public sealed class TreasuryAccessRequestsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<TreasuryAccessRequestListItem>>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? treasuryId,
        [FromQuery] bool mine,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListTreasuryAccessRequestsQuery(status, treasuryId, mine, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TreasuryAccessRequestDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTreasuryAccessRequestQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> RequestAccess([FromBody] RequestTreasuryAccessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new RequestTreasuryAccessCommand(request.TreasuryId, request.DeviceId, request.DeviceLabel), cancellationToken);
        return Created($"/api/v1/treasury-access-requests/{created.Id}", created);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveTreasuryAccessRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new ApproveTreasuryAccessCommand(id, body?.Comment), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectTreasuryAccessRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new RejectTreasuryAccessCommand(id, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, [FromBody] RevokeTreasuryAccessRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new RevokeTreasuryAccessCommand(id, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelTreasuryAccessRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelTreasuryAccessRequestCommand(id, body?.Reason), cancellationToken);
        return NoContent();
    }
}
