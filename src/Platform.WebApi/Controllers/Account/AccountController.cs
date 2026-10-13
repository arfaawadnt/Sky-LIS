using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Application.Account.Commands;
using Platform.Application.Account.Queries;
using Platform.Contracts.Account;
using Platform.Domain.Account;
using Platform.WebApi.Security;

namespace Platform.WebApi.Controllers.Account;

/// <summary>
/// Self-service account (SRS CP-65 / SA-21) and the audit log helpers of CP-68 / SA-09. Dispatch only: the policy on
/// each request decides (D-04). The password change carries two <c>[CredentialField]</c> values opened by the
/// credential transport filter and is rate-limited like every credential check (02 §5.2).
/// </summary>
[ApiController]
[Route("api/v1/account")]
public sealed class AccountController(ISender sender) : ControllerBase
{
    /// <summary>The logo limit plus room for the multipart envelope; the command refuses anything above the limit itself.</summary>
    private const long MaxLogoRequestBytes = LaboratoryLogoFiles.MaxSizeBytes + (64 * 1024);

    [HttpGet("profile")]
    public async Task<ActionResult<MyAccountProfile>> GetProfile(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMyAccountProfileQuery(), cancellationToken));

    /// <summary>The laboratory's logo for the top bar (inline; any signed-in member of the laboratory).</summary>
    [HttpGet("laboratory/logo")]
    public async Task<IActionResult> GetLaboratoryLogo(CancellationToken cancellationToken)
    {
        var logo = await sender.Send(new GetLaboratoryLogoQuery(), cancellationToken);
        return File(logo.Content, logo.ContentType);
    }

    /// <summary>Uploads or replaces the laboratory's logo (multipart field <c>file</c>; PNG, JPEG or WebP up to 512 KB).</summary>
    [HttpPost("laboratory/logo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxLogoRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxLogoRequestBytes)]
    public async Task<ActionResult<LaboratoryLogoView>> UploadLaboratoryLogo(IFormFile? file, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream() ?? Stream.Null;
        return Ok(await sender.Send(new UploadLaboratoryLogoCommand(content), cancellationToken));
    }

    [HttpDelete("laboratory/logo")]
    public async Task<IActionResult> RemoveLaboratoryLogo(CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveLaboratoryLogoCommand(), cancellationToken);
        return NoContent();
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateMyPreferencesCommand(request.Language, request.ResultsDensity), cancellationToken);
        return NoContent();
    }

    [HttpPost("password")]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new ChangeMyPasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken);
        return NoContent();
    }

    [HttpGet("audit/actors")]
    public async Task<ActionResult<IReadOnlyList<AuditActorItem>>> ListAuditActors(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAuditActorsQuery(), cancellationToken));

    /// <summary>CSV of the newest entries of the filter (UTF-8 with BOM); <c>audit.export</c>.</summary>
    [HttpGet("audit/export")]
    public async Task<IActionResult> ExportAudit(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? userId,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? language,
        [FromQuery] Guid? clientTenantId,
        CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportAuditEntriesQuery(from, to, userId, entityType, entityId, language, clientTenantId), cancellationToken);
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}
