using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.PlatformAdmin;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>
/// LIS-CR-012 (C-16): the SA-04 "Subscription" filter and the list behind the SA-03 "Expiring soon" tile. Dispatch only: the
/// policy (<c>platform.view</c>) and the platform-tenant check decide (PLT-403).
/// </summary>
[ApiController]
[Route("api/v1/platform/clients/subscriptions")]
public sealed class PlatformClientSubscriptionsController(ISender sender) : ControllerBase
{
    /// <summary><paramref name="state"/>: expiring (default) | grace | expired; <paramref name="days"/>: 1…60, default 7 (expiring only).</summary>
    [HttpGet]
    public async Task<ActionResult<SubscriptionClientList>> List([FromQuery] string? state, [FromQuery] int? days, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSubscriptionClientsQuery(state, days), cancellationToken));
}
