using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Account.Queries;
using Platform.Contracts.PlatformAdmin;

namespace Platform.WebApi.Controllers.Account;

/// <summary>
/// LIS-CR-013 (C-17): the Welcome announcements of the dashboard card for the session's laboratory (audience resolved from the
/// tenant, opt-out honoured). Any signed-in member; the images and counters are the public endpoints'.
/// </summary>
[ApiController]
[Route("api/v1/account/announcements")]
public sealed class AccountAnnouncementsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicAnnouncement>>> List(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMyAnnouncementsQuery(), cancellationToken));
}
