using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.PlatformAdmin;
using Platform.WebApi.Security;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>
/// LIS-CR-005: the card and logo a laboratory's sign-in address (<c>/lab/{slug}</c>) shows before anyone signs in. Anonymous,
/// limited by the global chain; a miss of any kind is <c>404 PLT-404</c>.
/// </summary>
[ApiController]
[Route("api/v1/public/laboratories")]
[AllowAnonymous]
public sealed class PublicLaboratoriesController(ISender sender) : ControllerBase
{
    /// <summary>How long a browser may reuse a laboratory's logo without asking again (the ETag carries its version).</summary>
    public const int LogoMaxAgeSeconds = 300;

    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicLaboratory>> Get(string slug, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPublicLaboratoryQuery(slug), cancellationToken));

    [HttpGet("{slug}/logo")]
    public async Task<IActionResult> GetLogo(string slug, CancellationToken cancellationToken)
    {
        var logo = await sender.Send(new GetPublicLaboratoryLogoQuery(slug), cancellationToken);
        Response.Headers.CacheControl = $"public, max-age={LogoMaxAgeSeconds}";
        HttpContext.Items[SecurityHeadersMiddleware.PublicCacheItem] = true;
        return File(logo.Content, logo.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue($"\"logo-v{logo.Version}\""));
    }
}
