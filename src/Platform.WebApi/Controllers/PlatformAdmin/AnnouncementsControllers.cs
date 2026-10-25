using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Counters;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.WebApi.Security;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>SA-22 Announcements (LIS-CR-013, C-17): the Super Admin register, editor, activation and image. Dispatch only.</summary>
[ApiController]
[Route("api/v1/platform/announcements")]
public sealed class PlatformAnnouncementsController(ISender sender) : ControllerBase
{
    /// <summary>The image limit plus room for the multipart envelope; the command refuses anything above the limit itself.</summary>
    private const long MaxImageRequestBytes = Announcement.MaxImageBytes + (64 * 1024);

    [HttpGet]
    public async Task<ActionResult<Paged<AnnouncementView>>> List([FromQuery] string? search, [FromQuery] string? placement, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAnnouncementsQuery(search, placement, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AnnouncementView>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAnnouncementQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] AnnouncementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateAnnouncementCommand(request), cancellationToken);
        return Created($"/api/v1/platform/announcements/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AnnouncementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateAnnouncementCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetAnnouncementActiveCommand(id, Active: true), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetAnnouncementActiveCommand(id, Active: false), cancellationToken);
        return NoContent();
    }

    /// <summary>The image for the editor's preview (any announcement, inline).</summary>
    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken)
    {
        var image = await sender.Send(new GetAnnouncementImageQuery(id), cancellationToken);
        return File(image.Content, image.ContentType);
    }

    /// <summary>Uploads or replaces the image (multipart field <c>file</c>; PNG, JPEG or WebP up to 512 KB).</summary>
    [HttpPost("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    public async Task<ActionResult<AnnouncementImageView>> UploadImage(Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream() ?? Stream.Null;
        return Ok(await sender.Send(new UploadAnnouncementImageCommand(id, content), cancellationToken));
    }

    [HttpDelete("{id:guid}/image")]
    public async Task<IActionResult> RemoveImage(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveAnnouncementImageCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>SA-04 "Hide announcements for this client" (decision D-4): the client's opt-out, read and set by the Super Admin.</summary>
[ApiController]
[Route("api/v1/platform/clients/{clientId:guid}/announcements")]
public sealed class PlatformClientAnnouncementsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClientAnnouncementsSetting>> Get(Guid clientId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetClientAnnouncementsSettingQuery(clientId), cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Set(Guid clientId, [FromBody] ClientAnnouncementsSetting request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetClientAnnouncementsHiddenCommand(clientId, request.Hidden), cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// LIS-CR-013 (C-17): what a laboratory's sign-in page shows before anyone signs in — the live SignIn announcements of the
/// laboratory's audience and their images — and the two counters its browser reports. Anonymous, limited by the global chain.
/// The list and the image may be cached by browsers for five minutes (<c>Cache-Control: public</c>); the counters always answer 204.
/// </summary>
[ApiController]
[Route("api/v1/public/announcements")]
[AllowAnonymous]
public sealed class PublicAnnouncementsController(ISender sender) : ControllerBase
{
    /// <summary>How long a browser may reuse the list or an image without asking again.</summary>
    public const int MaxAgeSeconds = 300;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicAnnouncement>>> List([FromQuery] string? placement, [FromQuery] string? laboratory, CancellationToken cancellationToken)
    {
        var announcements = await sender.Send(new ListPublicAnnouncementsQuery(placement, laboratory), cancellationToken);
        AllowPublicCache();
        return Ok(announcements);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken)
    {
        var image = await sender.Send(new GetPublicAnnouncementImageQuery(id), cancellationToken);
        AllowPublicCache();
        return File(image.Content, image.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue($"\"announcement-v{image.Version}\""));
    }

    [HttpPost("{id:guid}/impression")]
    public async Task<IActionResult> Impression(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordAnnouncementCounterCommand(id, AnnouncementCounter.Impression), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/click")]
    public async Task<IActionResult> Click(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordAnnouncementCounterCommand(id, AnnouncementCounter.Click), cancellationToken);
        return NoContent();
    }

    private void AllowPublicCache()
    {
        Response.Headers.CacheControl = $"public, max-age={MaxAgeSeconds}";
        HttpContext.Items[SecurityHeadersMiddleware.PublicCacheItem] = true;
    }
}
