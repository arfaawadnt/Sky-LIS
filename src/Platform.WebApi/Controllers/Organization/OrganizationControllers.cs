using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Organization.Commands;
using Platform.Application.Organization.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;

namespace Platform.WebApi.Controllers.Organization;

/// <summary>Branches (SRS CP-36) and the branch-selection list (CP-04). Dispatch only: the policy on each request decides (D-04).</summary>
[ApiController]
[Route("api/v1/organization/branches")]
public sealed class OrganizationBranchesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<BranchListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListBranchesQuery(search, isActive, page, pageSize), cancellationToken));

    /// <summary>The branches the current session may select (any authenticated actor).</summary>
    [HttpGet("selectable")]
    public async Task<ActionResult<SelectableBranchesResponse>> Selectable(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSelectableBranchesQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BranchListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBranchQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateBranchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateBranchCommand(request.Code, request.NameEn, request.NameAr, request.CountryId, request.GovernorateId, request.Phone, request.Email, request.Currency), cancellationToken);
        return Created($"/api/v1/organization/branches/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBranchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateBranchCommand(id, request.NameEn, request.NameAr, request.CountryId, request.GovernorateId, request.Phone, request.Email, request.Currency), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateBranchCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateBranchCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Roles and privileges (SRS CP-37, SA-06): permissions, special rights and result rights are separate resources of the role.</summary>
[ApiController]
[Route("api/v1/organization/roles")]
public sealed class OrganizationRolesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<RoleListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListRolesQuery(search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RoleDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetRoleQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateRoleCommand(request.NameEn, request.NameAr, request.Description, request.PermissionKeys ?? []), cancellationToken);
        return Created($"/api/v1/organization/roles/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateRoleCommand(id, request.NameEn, request.NameAr, request.Description), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] SetRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetRolePermissionsCommand(id, request.PermissionKeys ?? []), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/special-rights")]
    public async Task<IActionResult> SetSpecialRights(Guid id, [FromBody] SpecialRightsDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetRoleSpecialRightsCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/result-rights")]
    public async Task<IActionResult> SetResultRights(Guid id, [FromBody] SetRoleResultRightsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetRoleResultRightsCommand(id, request.Rights ?? []), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ActivateRoleCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateRoleCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Staff accounts (SRS CP-38, SA-07). The route id is the platform user id; every action applies to the membership in the current tenant.</summary>
[ApiController]
[Route("api/v1/organization/users")]
public sealed class OrganizationUsersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<UserListItem>>> List([FromQuery] string? search, [FromQuery] Guid? roleId, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListUsersQuery(search, roleId, status, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetUserQuery(id), cancellationToken));

    /// <summary>The password arrives as a credential envelope, opened by the edge filter before this action runs (D-03).</summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(
            new CreateUserCommand(
                request.Username,
                request.Password,
                request.MustChangePassword,
                request.FullNameEn,
                request.FullNameAr,
                request.Email,
                request.Mobile,
                request.Designation,
                request.Shifts,
                request.RoleId,
                request.BranchIds,
                request.PasswordExpiresAtUtc,
                request.EmployeeCode,
                request.PreferredLanguage),
            cancellationToken);
        return Created($"/api/v1/organization/users/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(
            new UpdateUserCommand(id, request.FullNameEn, request.FullNameAr, request.Email, request.Mobile, request.Designation, request.Shifts, request.PreferredLanguage, request.PasswordExpiresAtUtc, request.MustChangePassword),
            cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignUserRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new AssignUserRoleCommand(id, request.RoleId), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/branches")]
    public async Task<IActionResult> AssignBranches(Guid id, [FromBody] AssignUserBranchesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new AssignUserBranchesCommand(id, request.BranchIds ?? []), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/overrides")]
    public async Task<IActionResult> SetOverrides(Guid id, [FromBody] SetUserOverridesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetUserOverridesCommand(id, request.AddedPermissionKeys ?? [], request.RemovedPermissionKeys ?? [], request.SpecialRights), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RestoreUserToRoleCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/lock")]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new LockUserCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new UnlockUserCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>The temporary password arrives as a credential envelope (D-03).</summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new ResetUserPasswordCommand(id, request.TemporaryPassword, request.MustChangePassword), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateUserCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateUserCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>The permission catalogue of the privilege matrix (CP-37) and the actor's own effective privileges.</summary>
[ApiController]
[Route("api/v1/organization")]
public sealed class OrganizationPrivilegesController(ISender sender) : ControllerBase
{
    [HttpGet("permission-catalogue")]
    public async Task<ActionResult<PermissionCatalogueResponse>> Catalogue(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPermissionCatalogueQuery(), cancellationToken));

    /// <summary>Any authenticated actor: what the current session may do (permission keys, special rights, result rights).</summary>
    [HttpGet("me/privileges")]
    public async Task<ActionResult<MyPrivilegesResponse>> MyPrivileges(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMyPrivilegesQuery(), cancellationToken));
}
