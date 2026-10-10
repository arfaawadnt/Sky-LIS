using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Platform.Application.Account.Commands;
using Platform.Application.Account.Queries;
using Platform.Contracts.Account;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Account;

/// <summary>Support tickets of the laboratory (SRS CP-67): one endpoint per transition of intake LIS-INT-ACC-001 §7.</summary>
[ApiController]
[Route("api/v1/support/tickets")]
public sealed class SupportTicketsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SupportTicketListItem>>> List(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] string? search,
        [FromQuery] bool mine,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSupportTicketsQuery(status, priority, search, mine, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupportTicketDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSupportTicketQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Open([FromBody] OpenSupportTicketRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new OpenSupportTicketCommand(request.Subject, request.Description, request.Priority), cancellationToken);
        return Created($"/api/v1/support/tickets/{created.Id}", created);
    }

    [HttpPost("{id:guid}/replies")]
    public async Task<ActionResult<CreatedResource>> Reply(Guid id, [FromBody] SupportTicketReplyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new ReplyToSupportTicketCommand(id, request.Text), cancellationToken);
        return Created($"/api/v1/support/tickets/{id}", created);
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReopenSupportTicketCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new CloseSupportTicketCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>The platform support desk (Super Admin portal): every laboratory's tickets; the handlers refuse any other tenant (SUP-403).</summary>
[ApiController]
[Route("api/v1/support/desk/tickets")]
public sealed class SupportDeskController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SupportTicketListItem>>> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListDeskTicketsQuery(tenantId, status, priority, search, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupportTicketDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDeskTicketQuery(id), cancellationToken));

    [HttpPost("{id:guid}/replies")]
    public async Task<ActionResult<CreatedResource>> Answer(Guid id, [FromBody] SupportTicketReplyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new AnswerSupportTicketCommand(id, request.Text), cancellationToken);
        return Created($"/api/v1/support/desk/tickets/{id}", created);
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new StartSupportTicketCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResolveSupportTicketRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new ResolveSupportTicketCommand(id, body?.Note), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new CloseSupportTicketAtDeskCommand(id), cancellationToken);
        return NoContent();
    }
}
