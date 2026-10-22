using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Tracker.Queries;
using Platform.Contracts.Tracker;

namespace Platform.WebApi.Controllers.Tracker;

/// <summary>
/// The Lab Tracker (change request LIS-CR-006): every visit of a laboratory day through registration → samples → results →
/// payment, with its stage, next step and time left derived on the server. Dispatch only (D-04).
/// </summary>
[ApiController]
[Route("api/v1/tracker")]
public sealed class TrackerController(ISender sender) : ControllerBase
{
    /// <summary>
    /// The visits of <c>day</c> (laboratory day <c>yyyy-MM-dd</c>, default today) at <c>branchId</c> (default the session
    /// branch), filtered by <c>stage</c>, <c>attention</c> (Delayed | Panic | Rejected | ReadyUnpaid), <c>view</c> (Reception |
    /// Verification | FrontDesk) and <c>search</c>; sorted by <c>sort</c> (timeLeft | registeredAt | accession | patient |
    /// stage) and <c>direction</c>, then paged. The counters cover the whole filtered day before the stage, attention and
    /// view filters.
    /// </summary>
    [HttpGet("visits")]
    public async Task<ActionResult<LabTrackerPage>> Visits(
        [FromQuery] DateOnly? day,
        [FromQuery] Guid? branchId,
        [FromQuery] string? stage,
        [FromQuery] string? attention,
        [FromQuery] string? view,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetLabTrackerQuery(day, branchId, stage, attention, view, search, page, pageSize, sort, direction), cancellationToken));
}
