using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Jobs;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>Subscription plans (SRS SA-05).</summary>
[ApiController]
[Route("api/v1/platform/plans")]
public sealed class PlatformPlansController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PlanListItem>>> List([FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPlansQuery(isActive, page, pageSize), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] PlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreatePlanCommand(request.Code, request.NameEn, request.NameAr, request.MonthlyFee, request.CurrencyCode, request.MaxBranches, request.MaxUsers, request.SmsQuota, request.MaxAnalysers), cancellationToken);
        return Created($"/api/v1/platform/plans/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdatePlanCommand(id, request.NameEn, request.NameAr, request.MonthlyFee, request.CurrencyCode, request.MaxBranches, request.MaxUsers, request.SmsQuota, request.MaxAnalysers), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetPlanActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetPlanActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }
}

/// <summary>Client wallets (SRS SA-05): balance, transactions, top-up, the laboratories' requests.</summary>
[ApiController]
[Route("api/v1/platform/wallets")]
public sealed class PlatformWalletsController(ISender sender) : ControllerBase
{
    [HttpGet("{tenantId:guid}")]
    public async Task<ActionResult<WalletDetail>> Get(Guid tenantId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetClientWalletQuery(tenantId), cancellationToken));

    [HttpGet("{tenantId:guid}/transactions")]
    public async Task<ActionResult<Paged<WalletTransactionItem>>> Transactions(Guid tenantId, [FromQuery] string? kind, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListClientWalletTransactionsQuery(tenantId, kind, page, pageSize), cancellationToken));

    [HttpPost("{tenantId:guid}/top-up")]
    public async Task<ActionResult<CreatedResource>> TopUp(Guid tenantId, [FromBody] TopUpWalletRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new TopUpWalletCommand(tenantId, request.Amount, request.Reference, request.Note, request.TopUpRequestId), cancellationToken);
        return Created($"/api/v1/platform/wallets/{tenantId}/transactions", created);
    }

    /// <summary>SA-05 "Run charges now" (W2): the hosted monthly charge pass, on demand; returns charged / skipped / failed.</summary>
    [HttpPost("charge-run")]
    public async Task<ActionResult<WalletChargeRunSummary>> ChargeRun(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RunWalletChargesCommand(), cancellationToken));

    [HttpPost("{tenantId:guid}/top-up-requests/{requestId:guid}/decline")]
    public async Task<IActionResult> Decline(Guid tenantId, Guid requestId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeclineTopUpRequestCommand(tenantId, requestId), cancellationToken);
        return NoContent();
    }
}

/// <summary>The laboratory's own wallet (SRS CP-66), client tenants only: read and request a top-up.</summary>
[ApiController]
[Route("api/v1/wallet")]
public sealed class WalletController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WalletDetail>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMyWalletQuery(), cancellationToken));

    [HttpGet("transactions")]
    public async Task<ActionResult<Paged<WalletTransactionItem>>> Transactions([FromQuery] string? kind, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListMyWalletTransactionsQuery(kind, page, pageSize), cancellationToken));

    [HttpPost("top-up-requests")]
    public async Task<ActionResult<CreatedResource>> RequestTopUp([FromBody] CreateTopUpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new RequestWalletTopUpCommand(request.Amount, request.Note), cancellationToken);
        return Created("/api/v1/wallet", created);
    }
}
