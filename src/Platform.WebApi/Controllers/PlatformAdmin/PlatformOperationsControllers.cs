using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.PlatformAdmin;
using Platform.WebApi.Security;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>The Super Admin dashboard (SRS SA-03).</summary>
[ApiController]
[Route("api/v1/platform/dashboard")]
public sealed class PlatformDashboardController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlatformDashboard>> Get([FromQuery] string? period, [FromQuery] int? silentDays, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPlatformDashboardQuery(period, silentDays), cancellationToken));
}

/// <summary>Live sessions of the platform administrators (SRS SA-08).</summary>
[ApiController]
[Route("api/v1/platform/sessions")]
public sealed class PlatformSessionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminSessionItem>>> List([FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAdminSessionsQuery(search), cancellationToken));

    [HttpPost("{id:guid}/terminate")]
    public async Task<IActionResult> Terminate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new TerminateAdminSessionCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// The public subscription wizard (SRS CP-02): anonymous and rate-limited like sign-in (02 §5.2 — the account policy,
/// keyed on the client address here, and the challenge policy for the lookups). The password of step 3 travels in
/// the credential envelope (D-03, <c>[CredentialField]</c>).
/// </summary>
[ApiController]
[Route("api/v1/public/subscriptions")]
[AllowAnonymous]
public sealed class PublicSubscriptionsController(ISender sender) : ControllerBase
{
    [HttpPost("start")]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<ActionResult<SubscriptionStarted>> Start([FromBody] StartSubscriptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(
            new StartSubscriptionCommand(request.LabNameEn, request.LabNameAr, request.CountryId, request.GovernorateId, request.Address, request.Mobile, request.LicenceNo, request.ManagerName, request.ManagerMobile, request.Email, request.Language,
                request.CityId, request.DistrictId, request.AreaId),
            cancellationToken));
    }

    [HttpPost("resend")]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<ActionResult<SubscriptionStarted>> Resend([FromBody] ResendSubscriptionCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new ResendSubscriptionCodeCommand(request.SubscriptionId), cancellationToken));
    }

    [HttpPost("verify")]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<IActionResult> Verify([FromBody] VerifySubscriptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new VerifySubscriptionCommand(request.SubscriptionId, request.Code), cancellationToken);
        return NoContent();
    }

    [HttpPost("complete")]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<ActionResult<SubscriptionCompletedResponse>> Complete([FromBody] CompleteSubscriptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new CompleteSubscriptionCommand(request.SubscriptionId, request.Username, request.Password), cancellationToken));
    }

    /// <summary>LIS-CR-014 S1: the step-1 mobiles checked before the code is sent (per-field issues; rate-limited like start).</summary>
    [HttpPost("check")]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<ActionResult<SubscriptionCheckResult>> Check([FromBody] CheckSubscriptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new CheckSubscriptionDetailsQuery(request.CountryId, request.Mobile, request.ManagerMobile), cancellationToken));
    }

    /// <summary>LIS-CR-014 S2: step 3's username check for a verified request (issues CLI-042 / SUB-013).</summary>
    [HttpGet("{id:guid}/username-available")]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<SubscriptionCheckResult>> UsernameAvailable(Guid id, [FromQuery] string? username, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new CheckSubscriptionUsernameQuery(id, username ?? string.Empty), cancellationToken));

    [HttpGet("check-username")]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<UsernameAvailability>> CheckUsername([FromQuery] string? username, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new CheckUsernameQuery(username ?? string.Empty), cancellationToken));
}

/// <summary>Reference lookups of the public wizard: active countries and their governorates.</summary>
[ApiController]
[Route("api/v1/public")]
[AllowAnonymous]
public sealed class PublicReferenceController(ISender sender) : ControllerBase
{
    [HttpGet("countries")]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<IReadOnlyList<PublicCountry>>> Countries(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPublicCountriesQuery(), cancellationToken));

    [HttpGet("governorates")]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<IReadOnlyList<PublicGovernorate>>> Governorates([FromQuery] Guid? countryId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPublicGovernoratesQuery(countryId), cancellationToken));

    /// <summary>Active cities of a governorate (change request 2026-10-19 A2; the patient form's pick list).</summary>
    [HttpGet("cities")]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<IReadOnlyList<PublicCity>>> Cities([FromQuery] Guid? governorateId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPublicCitiesQuery(governorateId), cancellationToken));

    [HttpGet("districts")]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<IReadOnlyList<PublicDistrict>>> Districts([FromQuery] Guid? cityId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPublicDistrictsQuery(cityId), cancellationToken));

    [HttpGet("areas")]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<IReadOnlyList<PublicArea>>> Areas([FromQuery] Guid? districtId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPublicAreasQuery(districtId), cancellationToken));
}
