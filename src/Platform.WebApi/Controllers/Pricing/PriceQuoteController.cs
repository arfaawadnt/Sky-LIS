using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Pricing.Commands;
using Platform.Application.Pricing.Queries;
using Platform.Contracts.Pricing;

namespace Platform.WebApi.Controllers.Pricing;

/// <summary>
/// The price query of CP-12 (BR-06): <c>GET /api/v1/pricing/quote?payerId&amp;tierId&amp;serviceIds=a&amp;serviceIds=b</c>.
/// Without payer and tier the quote is for the tenant's cash payer, which is provisioned on first use (intake Q1), so
/// the answer always carries the payer and tier ids registration stores.
/// </summary>
[ApiController]
[Route("api/v1/pricing/quote")]
public sealed class PriceQuoteController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PriceQuote>> Quote([FromQuery] Guid? payerId, [FromQuery] Guid? tierId, [FromQuery] Guid[]? serviceIds, CancellationToken cancellationToken)
    {
        if (payerId is null && tierId is null)
        {
            var cash = await sender.Send(new EnsureDefaultPayerCommand(), cancellationToken);
            (payerId, tierId) = (cash.PayerId, cash.ContractTierId);
        }

        return Ok(await sender.Send(new QuotePricesQuery(payerId, tierId, serviceIds ?? []), cancellationToken));
    }
}
