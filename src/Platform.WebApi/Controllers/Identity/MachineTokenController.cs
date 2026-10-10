using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Application.Identity.MachineCredentials.Commands;
using Platform.Application.Identity.MachineCredentials.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.WebApi.Security;

namespace Platform.WebApi.Controllers.Identity;

/// <summary>
/// Machine clients (02 §4.2, §4.3, §6.3): the token exchange and the body-borne refresh (anonymous, <c>machine-token</c>
/// limiter keyed by the client id; no cookie, no CSRF header — the token travels in the body, 02 §4.3a), and the
/// management of the tenant's machine credentials. The client secret of the exchange is a credential field, opened from
/// its envelope by the edge filter (D-03). Dispatch only.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class MachineTokenController(ISender sender) : ControllerBase
{
    private const string Credentials = "identity/machine-credentials";

    [HttpPost("auth/token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.MachineToken)]
    public async Task<ActionResult<MachineTokenResponse>> PostToken([FromBody] MachineTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new ExchangeMachineTokenCommand(request.ClientId, request.ClientSecret, ClientIp(), UserAgent()), cancellationToken);
        return Ok(ToResponse(result));
    }

    [HttpPost("auth/token/refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.MachineToken)]
    public async Task<ActionResult<MachineTokenResponse>> PostRefresh([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MachineTokenRefreshRequest? request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RefreshMachineTokenCommand(request?.RefreshToken, ClientIp(), UserAgent()), cancellationToken);
        return Ok(ToResponse(result));
    }

    [HttpGet(Credentials)]
    public async Task<ActionResult<Paged<MachineCredentialListItem>>> List([FromQuery] bool includeRevoked, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListMachineCredentialsQuery(includeRevoked, page, pageSize), cancellationToken));

    [HttpPost(Credentials)]
    public async Task<ActionResult<MachineCredentialSecretResponse>> Create([FromBody] CreateMachineCredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateMachineCredentialCommand(request.Name, request.Scopes ?? [], request.ExpiresAtUtc), cancellationToken);
        return Created($"/api/v1/{Credentials}/{created.Id}", created);
    }

    [HttpPost(Credentials + "/{id:guid}/rotate")]
    public async Task<ActionResult<MachineCredentialSecretResponse>> Rotate(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RotateMachineCredentialCommand(id), cancellationToken));

    [HttpPost(Credentials + "/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RevokeMachineCredentialRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new RevokeMachineCredentialCommand(id, body?.Reason), cancellationToken);
        return NoContent();
    }

    private static MachineTokenResponse ToResponse(MachineTokenResult result) =>
        new(result.AccessToken.Token, result.AccessToken.ExpiresAtUtc, result.RefreshToken, "Bearer", result.Scopes);

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent()
    {
        var value = Request.Headers.UserAgent.ToString();
        return value.Length == 0 ? null : value.Length <= RefreshSession.MaxUserAgentLength ? value : value[..RefreshSession.MaxUserAgentLength];
    }
}
