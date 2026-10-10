using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Settings.Commands;
using Platform.Application.Settings.Queries;
using Platform.Contracts.Settings;

namespace Platform.WebApi.Controllers.Settings;

/// <summary>Tenant parameters (SRS CP-57): read with <c>settings.view</c>, edit with <c>settings.manage</c>; the PUT answers with the effective list.</summary>
[ApiController]
[Route("api/v1/settings")]
public sealed class SettingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TenantSettingsResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTenantSettingsQuery(), cancellationToken));

    [HttpPut]
    public async Task<ActionResult<TenantSettingsResponse>> Put([FromBody] UpdateTenantSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new UpdateTenantSettingsCommand(request.Settings), cancellationToken));
    }
}
