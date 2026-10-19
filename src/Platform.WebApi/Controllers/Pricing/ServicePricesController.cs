using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Pricing.Queries;
using Platform.Contracts.Pricing;

namespace Platform.WebApi.Controllers.Pricing;

/// <summary>Every price of one catalogue service (CP-33b Service prices, CR 2026-10-19 C22). Dispatch only (D-04).</summary>
[ApiController]
[Route("api/v1/pricing/service-prices")]
public sealed class ServicePricesController(ISender sender) : ControllerBase
{
    [HttpGet("{serviceId:guid}")]
    public async Task<ActionResult<ServicePricesView>> Get(Guid serviceId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetServicePricesQuery(serviceId), cancellationToken));
}
