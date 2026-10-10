using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Platform.Application.Pricing.Commands;
using Platform.Application.Pricing.Queries;
using Platform.Contracts.Pricing;

namespace Platform.WebApi.Controllers.Pricing;

/// <summary>Contract pricing of a tier (SRS CP-34), addressed by the tier id (one pricing per tier). Dispatch only (D-04).</summary>
[ApiController]
[Route("api/v1/pricing/contract-pricing")]
public sealed class ContractPricingController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContractPricingListItem>>> List([FromQuery] Guid? payerId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListContractPricingsQuery(payerId), cancellationToken));

    [HttpGet("{tierId:guid}")]
    public async Task<ActionResult<ContractPricingDetail>> Get(Guid tierId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetContractPricingQuery(tierId), cancellationToken));

    /// <summary>C1: sets the formula and effective dates (creates the tier's pricing on first use).</summary>
    [HttpPut("{tierId:guid}")]
    public async Task<IActionResult> SetFormula(Guid tierId, [FromBody] SetContractFormulaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetContractFormulaCommand(tierId, request.EffectiveFrom, request.EffectiveTo, request.Terms ?? []), cancellationToken);
        return NoContent();
    }

    /// <summary>C2 Apply (BR-07): recomputes the tier; excluded prices are kept.</summary>
    [HttpPost("{tierId:guid}/apply")]
    public async Task<ActionResult<ContractApplyResult>> Apply(Guid tierId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ApplyContractPricingCommand(tierId), cancellationToken));

    /// <summary>C3: a negotiated price, kept through every Apply.</summary>
    [HttpPut("{tierId:guid}/prices/{serviceId:guid}")]
    public async Task<IActionResult> Negotiate(Guid tierId, Guid serviceId, [FromBody] NegotiatePriceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new NegotiateContractPriceCommand(tierId, serviceId, request.PatientShare, request.PayerShare), cancellationToken);
        return NoContent();
    }

    /// <summary>C4: lifts the exclusion and restores the price from the formula.</summary>
    [HttpDelete("{tierId:guid}/prices/{serviceId:guid}/exclusion")]
    public async Task<IActionResult> ClearExclusion(Guid tierId, Guid serviceId, CancellationToken cancellationToken)
    {
        await sender.Send(new ClearPriceExclusionCommand(tierId, serviceId), cancellationToken);
        return NoContent();
    }

    /// <summary>C5: sets (or, with an empty text, clears) the alert condition shown at ordering.</summary>
    [HttpPut("{tierId:guid}/prices/{serviceId:guid}/alert")]
    public async Task<IActionResult> SetAlert(Guid tierId, Guid serviceId, [FromBody] SetPriceAlertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetPriceAlertCommand(tierId, serviceId, request.AlertCondition), cancellationToken);
        return NoContent();
    }

    /// <summary>C6: copies another tier's formula, prices, exclusions and alerts, optionally scaled.</summary>
    [HttpPost("{tierId:guid}/copy-from/{otherTierId:guid}")]
    public async Task<IActionResult> CopyFrom(Guid tierId, Guid otherTierId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CopyContractPricingRequest? request, CancellationToken cancellationToken)
    {
        await sender.Send(new CopyContractPricingCommand(tierId, otherTierId, request?.Factor), cancellationToken);
        return NoContent();
    }

    /// <summary>Data of the printable review report.</summary>
    [HttpGet("{tierId:guid}/review")]
    public async Task<ActionResult<ContractPricingReviewDocument>> Review(Guid tierId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetContractPricingReviewQuery(tierId), cancellationToken));
}
