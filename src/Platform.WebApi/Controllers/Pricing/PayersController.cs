using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Pricing.Commands;
using Platform.Application.Pricing.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;

namespace Platform.WebApi.Controllers.Pricing;

/// <summary>Payers and their contract tiers (SRS CP-32). Dispatch only: the policy on each request decides who may call it (D-04).</summary>
[ApiController]
[Route("api/v1/pricing/payers")]
public sealed class PayersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PayerListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPayersQuery(search, type, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PayerListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPayerQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreatePayerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreatePayerCommand(request.NameEn, request.NameAr, request.SearchKey, request.Type, request.WebPortalEnabled, request.WebUsername), cancellationToken);
        return Created($"/api/v1/pricing/payers/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePayerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdatePayerCommand(id, request.NameEn, request.NameAr, request.SearchKey, request.Type, request.WebPortalEnabled, request.WebUsername), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivatePayerCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivatePayerCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>The tenant's cash payer and default tier, created on first call (intake Q1).</summary>
    [HttpPost("default")]
    public async Task<ActionResult<DefaultPayerIds>> EnsureDefault(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new EnsureDefaultPayerCommand(), cancellationToken));

    /// <summary>Every tier with its payer, for pickers; <paramref name="activeOnly"/> drops inactive tiers and payers.</summary>
    [HttpGet("tiers")]
    public async Task<ActionResult<IReadOnlyList<TierOption>>> TierOptions([FromQuery] bool activeOnly, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListTierOptionsQuery(activeOnly), cancellationToken));

    [HttpGet("{id:guid}/tiers")]
    public async Task<ActionResult<IReadOnlyList<ContractTierItem>>> Tiers(Guid id, CancellationToken cancellationToken) =>
        Ok((await sender.Send(new GetPayerQuery(id), cancellationToken)).Tiers);

    [HttpPost("{id:guid}/tiers")]
    public async Task<ActionResult<CreatedResource>> AddTier(Guid id, [FromBody] ContractTierRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new AddContractTierCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/pricing/payers/{id}/tiers/{created.Id}", created);
    }

    [HttpPut("{id:guid}/tiers/{tierId:guid}")]
    public async Task<IActionResult> RenameTier(Guid id, Guid tierId, [FromBody] ContractTierRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new RenameContractTierCommand(id, tierId, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/tiers/{tierId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateTier(Guid id, Guid tierId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateContractTierCommand(id, tierId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/tiers/{tierId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateTier(Guid id, Guid tierId, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateContractTierCommand(id, tierId), cancellationToken);
        return NoContent();
    }
}
