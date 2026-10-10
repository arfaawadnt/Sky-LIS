using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Account.Queries;
using Platform.Contracts.Account;

namespace Platform.WebApi.Controllers.Account;

/// <summary>The branch dashboard (SRS CP-05). <c>dashboard.view</c>; the branch is the session's (TENANT-004 without one).</summary>
[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    /// <summary><paramref name="date"/> is the local day (yyyy-MM-dd, default today); <paramref name="utcOffsetMinutes"/> the browser's offset from UTC.</summary>
    [HttpGet("branch")]
    public async Task<ActionResult<BranchDashboard>> GetBranch([FromQuery] string? date, [FromQuery] int? utcOffsetMinutes, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBranchDashboardQuery(date, utcOffsetMinutes), cancellationToken));
}
