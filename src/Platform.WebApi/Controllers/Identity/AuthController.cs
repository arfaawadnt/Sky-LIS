using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Application.Abstractions.CredentialTransport;
using Platform.Application.Identity.Commands;
using Platform.Application.Identity.Queries;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.WebApi.Security;

namespace Platform.WebApi.Controllers.Identity;

/// <summary>
/// The authentication surface of 02 §4. Controllers inject only <see cref="ISender"/> (01 §2.1); the cookie is the
/// host's concern, so this controller writes and clears it from the handler results. Nothing here decides anything.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpGet("credential-challenge")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialChallenge)]
    public async Task<ActionResult<CredentialChallengeResponse>> GetCredentialChallenge(CancellationToken cancellationToken)
    {
        var challenge = await sender.Send(new IssueCredentialChallengeQuery(), cancellationToken);
        return Ok(new CredentialChallengeResponse(
            challenge.Kid,
            new RsaPublicJwk("RSA", challenge.PublicKeyModulus, challenge.PublicKeyExponent, CredentialChallenge.Algorithm, "enc", challenge.Kid),
            challenge.PublicKeyPem,
            CredentialChallenge.Algorithm,
            CredentialChallenge.Encryption,
            challenge.Nonce,
            challenge.ExpiresAtUtc));
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<ActionResult<AuthResponse>> PostSignIn([FromBody] SignInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new SignInCommand(request.Tenant, request.Username, request.Password, ClientIp(), UserAgent()), cancellationToken);

        if (result.Step == AuthResponse.StepSignedIn && result.RefreshToken is not null && result.Kind == SessionKind.Browser)
        {
            RefreshCookie.Write(HttpContext, result.RefreshToken);
        }

        return Ok(new AuthResponse(result.Step, result.AccessToken?.Token, result.AccessToken?.ExpiresAtUtc, null, result.Profile, result.Tenants));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Refresh)]
    public async Task<ActionResult<AuthResponse>> PostRefresh([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? body, CancellationToken cancellationToken)
    {
        var fromCookie = RefreshCookie.Read(HttpContext);
        var result = await sender.Send(new RefreshSessionCommand(fromCookie ?? body?.RefreshToken, ClientIp(), UserAgent()), cancellationToken);

        if (fromCookie is not null)
        {
            RefreshCookie.Write(HttpContext, result.RefreshToken);
            return Ok(new AuthResponse(AuthResponse.StepSignedIn, result.AccessToken.Token, result.AccessToken.ExpiresAtUtc, null, result.Profile, null));
        }

        return Ok(new AuthResponse(AuthResponse.StepSignedIn, result.AccessToken.Token, result.AccessToken.ExpiresAtUtc, result.RefreshToken, result.Profile, null));
    }

    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> PostSignOut([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SignOutRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new SignOutCommand(RefreshCookie.Read(HttpContext) ?? body?.RefreshToken), cancellationToken);
        RefreshCookie.Clear(HttpContext);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<SessionResponse>> GetMe(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMySessionQuery(), cancellationToken));

    /// <summary>CP-04: binds the session to one of the membership's branches and returns the refreshed profile.</summary>
    [HttpPost("select-branch")]
    public async Task<ActionResult<AuthProfile>> PostSelectBranch([FromBody] SelectBranchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new SelectBranchCommand(request.BranchId), cancellationToken));
    }

    /// <summary>CP-03 step 1: always 202; a one-time code goes to the registered mobile when the account exists.</summary>
    [HttpPost("recovery/start")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<IActionResult> PostRecoveryStart([FromBody] StartPasswordRecoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new StartPasswordRecoveryCommand(request.UsernameOrMobile, request.Language, ClientIp()), cancellationToken);
        return Accepted();
    }

    /// <summary>CP-03 step 2: the code and the enveloped new password; 204 on success, 401 AUTH-013 on any refusal.</summary>
    [HttpPost("recovery/complete")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialAccount)]
    public async Task<IActionResult> PostRecoveryComplete([FromBody] CompletePasswordRecoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new CompletePasswordRecoveryCommand(request.UsernameOrMobile, request.Code, request.NewPassword), cancellationToken);
        return NoContent();
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent()
    {
        var value = Request.Headers.UserAgent.ToString();
        return value.Length == 0 ? null : value.Length <= RefreshSession.MaxUserAgentLength ? value : value[..RefreshSession.MaxUserAgentLength];
    }
}
