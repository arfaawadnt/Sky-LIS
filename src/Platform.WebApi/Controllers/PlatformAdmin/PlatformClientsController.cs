using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>Clients of the Super Admin portal (SRS SA-04). Dispatch only: the policy and the platform-tenant check decide (D-04, PLT-403).</summary>
[ApiController]
[Route("api/v1/platform/clients")]
public sealed class PlatformClientsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<ClientListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] string? source,
        [FromQuery] Guid? planId,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListClientsQuery(search, source, planId, status, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetClientQuery(id), cancellationToken));

    /// <summary>SA-04 "Add": provisions the client, its wallet, branch, administrator and master data in one transaction.</summary>
    [HttpPost]
    public async Task<ActionResult<ProvisionedClient>> Provision([FromBody] ProvisionClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(
            new ProvisionClientCommand(
                request.NameEn, request.NameAr, request.CountryId, request.GovernorateId, request.Address, request.Mobile, request.LicenceNo, request.ManagerName,
                request.ManagerMobile, request.Email, request.PlanId, request.PortalSlug, request.Language, request.AdminUsername, request.AdminPassword),
            cancellationToken);
        return Created($"/api/v1/platform/clients/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(
            new UpdateClientCommand(id, request.NameEn, request.NameAr, request.CountryId, request.GovernorateId, request.Address, request.Mobile, request.LicenceNo, request.ManagerName, request.ManagerMobile, request.Email, request.PlanId, request.Language),
            cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ActivateClientCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, [FromBody] DeactivateClientRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new DeactivateClientCommand(id, body.Reason), cancellationToken);
        return NoContent();
    }

    /// <summary>SA-04 "Reset password": the new password arrives sealed in the credential envelope (D-03).</summary>
    [HttpPost("{id:guid}/reset-admin-password")]
    public async Task<IActionResult> ResetAdminPassword(Guid id, [FromBody] ResetClientAdminPasswordRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new ResetClientAdminPasswordCommand(id, body.NewPassword), cancellationToken);
        return NoContent();
    }
}
