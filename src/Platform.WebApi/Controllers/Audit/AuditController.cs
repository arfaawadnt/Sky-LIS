using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Audit.Queries;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Audit;

/// <summary>The audit log (SRS CP-68 client, SA-09 platform) and the ledger check of 01 §4.8. Read-only; <c>audit.view</c> decides.</summary>
[ApiController]
[Route("api/v1/audit")]
public sealed class AuditController(ISender sender) : ControllerBase
{
    [HttpGet("entries")]
    public async Task<ActionResult<Paged<AuditEntryItem>>> ListEntries(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? userId,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? clientTenantId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAuditEntriesQuery(from, to, userId, entityType, entityId, page, pageSize, clientTenantId), cancellationToken));

    /// <summary>The clients the platform log can be filtered by (SA-09; empty in a laboratory).</summary>
    [HttpGet("clients")]
    public async Task<ActionResult<IReadOnlyList<AuditClientItem>>> ListClients(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAuditClientsQuery(), cancellationToken));

    [HttpGet("verify-chain")]
    public async Task<ActionResult<ChainVerificationResult>> VerifyChain(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new VerifyAuditChainQuery(), cancellationToken));
}
