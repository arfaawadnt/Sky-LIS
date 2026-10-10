using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Integration.Commands;
using Platform.Application.Integration.Queries;
using Platform.Contracts.Integration;

namespace Platform.WebApi.Controllers.Integration;

/// <summary>
/// The site agent API (intake rows H1, R1, R2): bearer machine tokens from <c>POST /api/v1/auth/token</c>; the requests
/// carry integration.create (writes) or integration.view (reads) and the handlers accept only an active site agent
/// (INT-035). Dispatch only.
/// </summary>
[ApiController]
[Route("api/v1/agents")]
public sealed class AgentsController(ISender sender) : ControllerBase
{
    [HttpPost("heartbeat")]
    public async Task<ActionResult<AgentHeartbeatResponse>> Heartbeat([FromBody] AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new RecordAgentHeartbeatCommand(request), cancellationToken));
    }

    [HttpPost("logs")]
    public async Task<ActionResult<AgentLogBatchResponse>> Logs([FromBody] AgentLogBatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new AppendAgentLogsCommand(request), cancellationToken));
    }

    [HttpGet("config")]
    public async Task<ActionResult<AgentConfigView>> Config(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAgentConfigQuery(), cancellationToken));

    [HttpGet("orders")]
    public async Task<ActionResult<AgentOrdersView>> Orders([FromQuery] string analyserCode, [FromQuery] DateTimeOffset? since, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAgentOrdersQuery(analyserCode ?? string.Empty, since), cancellationToken));

    [HttpPost("results")]
    public async Task<ActionResult<AgentResultBatchResponse>> Results([FromBody] AgentResultBatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new SubmitAgentResultsCommand(request), cancellationToken));
    }

    [HttpPost("pcr-runs")]
    public async Task<ActionResult<AgentPcrRunResponse>> PcrRuns([FromBody] AgentPcrRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new SubmitPcrRunCommand(request), cancellationToken));
    }
}
