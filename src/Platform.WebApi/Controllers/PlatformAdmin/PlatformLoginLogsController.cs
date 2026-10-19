using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>
/// Client login logs of the Super Admin portal (SA-08b, change request LIS-CR-003 §2): the history of the client users'
/// browser sessions, paged and sorted on the server, and its CSV. Dispatch only: the policy (<c>platform.view</c>) and
/// the platform-tenant check decide (D-04, PLT-403).
/// </summary>
[ApiController]
[Route("api/v1/platform/login-logs")]
public sealed class PlatformLoginLogsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<ClientLoginLogItem>>> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? search,
        [FromQuery] DateTimeOffset? loginFromUtc,
        [FromQuery] DateTimeOffset? loginToUtc,
        [FromQuery] DateTimeOffset? logoutFromUtc,
        [FromQuery] DateTimeOffset? logoutToUtc,
        [FromQuery] string? device,
        [FromQuery] int? minMinutes,
        [FromQuery] int? maxMinutes,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new ListClientLoginLogsQuery(tenantId, search, loginFromUtc, loginToUtc, logoutFromUtc, logoutToUtc, device, minMinutes, maxMinutes, status, page, pageSize, sort, direction),
            cancellationToken));

    /// <summary>CSV of the filtered history (UTF-8 with BOM, at most 10 000 rows); the same filters as the list, headers in <paramref name="language"/>.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? search,
        [FromQuery] DateTimeOffset? loginFromUtc,
        [FromQuery] DateTimeOffset? loginToUtc,
        [FromQuery] DateTimeOffset? logoutFromUtc,
        [FromQuery] DateTimeOffset? logoutToUtc,
        [FromQuery] string? device,
        [FromQuery] int? minMinutes,
        [FromQuery] int? maxMinutes,
        [FromQuery] string? status,
        [FromQuery] string? language,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        CancellationToken cancellationToken)
    {
        var file = await sender.Send(
            new ExportClientLoginLogsQuery(tenantId, search, loginFromUtc, loginToUtc, logoutFromUtc, logoutToUtc, device, minMinutes, maxMinutes, status, language, sort, direction),
            cancellationToken);
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}
