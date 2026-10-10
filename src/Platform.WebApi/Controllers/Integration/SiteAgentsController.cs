using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Integration.Commands;
using Platform.Application.Integration.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;

namespace Platform.WebApi.Controllers.Integration;

/// <summary>Site agents (SRS CP-61): register, credential (secret shown once), configuration push, logs. Dispatch only.</summary>
[ApiController]
[Route("api/v1/integration/site-agents")]
public sealed class SiteAgentsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SiteAgentListItem>>> List([FromQuery] string? search, [FromQuery] Guid? branchId, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSiteAgentsQuery(search, branchId, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SiteAgentDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSiteAgentQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<SiteAgentSecretResponse>> Create([FromBody] CreateSiteAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateSiteAgentCommand(request.Name, request.BranchId, request.ConfigJson), cancellationToken);
        return Created($"/api/v1/integration/site-agents/{created.AgentId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSiteAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateSiteAgentCommand(id, request.Name, request.ConfigJson), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/rotate-secret")]
    public async Task<ActionResult<SiteAgentSecretResponse>> RotateSecret(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RotateSiteAgentSecretCommand(id), cancellationToken));

    [HttpPost("{id:guid}/push-config")]
    public async Task<IActionResult> PushConfig(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new PushSiteAgentConfigCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateSiteAgentCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult<SiteAgentSecretResponse>> Reactivate(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ReactivateSiteAgentCommand(id), cancellationToken));

    [HttpGet("{id:guid}/logs")]
    public async Task<ActionResult<Paged<AgentLogItem>>> Logs(
        Guid id,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? level,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAgentLogsQuery(id, from, to, level, search, page, pageSize), cancellationToken));
}
