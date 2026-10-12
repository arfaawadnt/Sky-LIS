using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Platform.Application.Samples.Commands;
using Platform.Application.Samples.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;

namespace Platform.WebApi.Controllers.Samples;

/// <summary>
/// The sample track (SRS CP-14…CP-17, WF-05): registers, scan, bulk actions of an accession, reject / unreject /
/// replace, the timeline and the label documents. Dispatch only: the policy on each request decides who may call it
/// (D-04) and the Sample aggregate decides the rest; reasons travel in typed bodies (pitfall P-16).
/// </summary>
[ApiController]
[Route("api/v1/samples")]
public sealed class SamplesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SampleListItem>>> List(
        [FromQuery] Guid? visitId,
        [FromQuery] string? accession,
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? clinicalGroupId,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSamplesQuery(visitId, accession, status, from, to, branchId, clinicalGroupId, search, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SampleDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSampleQuery(id), cancellationToken));

    [HttpGet("by-number/{sampleNumber}")]
    public async Task<ActionResult<SampleDetail>> GetByNumber(string sampleNumber, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSampleByNumberQuery(sampleNumber), cancellationToken));

    [HttpGet("rejected-today")]
    public async Task<ActionResult<IReadOnlyList<SampleListItem>>> RejectedToday([FromQuery] Guid? branchId, [FromQuery] int? utcOffsetMinutes, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListRejectedTodayQuery(branchId, utcOffsetMinutes), cancellationToken));

    [HttpGet("settings")]
    public async Task<ActionResult<SampleSettingsView>> Settings(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSampleSettingsQuery(), cancellationToken));

    [HttpPost("scan")]
    public async Task<ActionResult<SampleDetail>> Scan([FromBody] ScanSampleRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new ScanSampleCommand(body.SampleNumber, body.Mode), cancellationToken));
    }

    [HttpPost("visits/{visitId:guid}/collect-all")]
    public async Task<ActionResult<VisitSamplesActionResult>> CollectAll(Guid visitId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new CollectVisitSamplesCommand(visitId), cancellationToken));

    [HttpPost("visits/{visitId:guid}/receive-all")]
    public async Task<ActionResult<VisitSamplesActionResult>> ReceiveAll(Guid visitId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ReceiveVisitSamplesCommand(visitId), cancellationToken));

    [HttpGet("visits/{visitId:guid}/documents/labels")]
    public async Task<ActionResult<VisitLabelsDocument>> VisitLabels(Guid visitId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetVisitLabelsQuery(visitId), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectSampleRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new RejectSampleCommand(id, body.ReasonId, body.Note), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/unreject")]
    public async Task<IActionResult> Unreject(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UnrejectSampleRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new UnrejectSampleCommand(id, body?.Note), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/replace")]
    public async Task<ActionResult<CreatedResource>> Replace(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReplaceSampleRequest? body, CancellationToken cancellationToken)
    {
        var created = await sender.Send(new ReplaceSampleCommand(id, body?.Note), cancellationToken);
        return Created($"/api/v1/samples/{created.Id}", created);
    }

    [HttpGet("{id:guid}/timeline")]
    public async Task<ActionResult<SampleTimeline>> Timeline(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSampleTimelineQuery(id), cancellationToken));

    [HttpGet("{id:guid}/documents/label")]
    public async Task<ActionResult<SampleLabelDocument>> Label(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSampleLabelQuery(id), cancellationToken));
}
