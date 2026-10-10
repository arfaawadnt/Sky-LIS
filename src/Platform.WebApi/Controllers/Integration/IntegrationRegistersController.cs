using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Integration.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;

namespace Platform.WebApi.Controllers.Integration;

/// <summary>Cross-cutting registers of the module: the result intake log (CP-61 tab) and the platform analyser models (SA-15, read). Dispatch only.</summary>
[ApiController]
[Route("api/v1/integration")]
public sealed class IntegrationRegistersController(ISender sender) : ControllerBase
{
    [HttpGet("intake")]
    public async Task<ActionResult<Paged<IntakeLogItem>>> Intake(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? status,
        [FromQuery] Guid? analyserId,
        [FromQuery] Guid? agentId,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListResultIntakeQuery(from, to, status, analyserId, agentId, search, page, pageSize), cancellationToken));

    [HttpGet("analyser-models")]
    public async Task<ActionResult<IReadOnlyList<AnalyserModelItem>>> AnalyserModels([FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAnalyserModelsQuery(search), cancellationToken));
}
