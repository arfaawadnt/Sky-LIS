using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Integration.Commands;
using Platform.Application.Integration.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;

namespace Platform.WebApi.Controllers.Integration;

/// <summary>Analysers (SRS CP-58), their test mappings (CP-59), sample codes (CP-60) and the code sheet document. Dispatch only (D-04).</summary>
[ApiController]
[Route("api/v1/integration/analysers")]
public sealed class AnalysersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<AnalyserListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? clinicalGroupId,
        [FromQuery] Guid? siteAgentId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAnalysersQuery(search, isActive, clinicalGroupId, siteAgentId, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AnalyserDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAnalyserQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] SaveAnalyserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateAnalyserCommand(request), cancellationToken);
        return Created($"/api/v1/integration/analysers/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveAnalyserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateAnalyserCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateAnalyserCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateAnalyserCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>"Test connection": the last state the hosting agent reported.</summary>
    [HttpGet("{id:guid}/connection")]
    public async Task<ActionResult<AnalyserConnectionState>> Connection(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAnalyserConnectionQuery(id), cancellationToken));

    [HttpGet("{id:guid}/documents/code-sheet")]
    [HttpGet("{id:guid}/mappings/code-sheet")]
    public async Task<ActionResult<AnalyserCodeSheetDocument>> CodeSheet(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAnalyserCodeSheetQuery(id), cancellationToken));

    [HttpGet("{id:guid}/mappings")]
    public async Task<ActionResult<Paged<MappingListItem>>> Mappings(Guid id, [FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAnalyserMappingsQuery(id, search, isActive, page, pageSize), cancellationToken));

    [HttpPost("{id:guid}/mappings")]
    public async Task<ActionResult<CreatedResource>> MapTest(Guid id, [FromBody] SaveMappingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new MapAnalyserTestCommand(id, request), cancellationToken);
        return Created($"/api/v1/integration/analysers/{id}/mappings/{created.Id}", created);
    }

    [HttpPut("{id:guid}/mappings/{mappingId:guid}")]
    public async Task<IActionResult> UpdateMapping(Guid id, Guid mappingId, [FromBody] SaveMappingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateAnalyserTestMappingCommand(id, mappingId, request), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/mappings/{mappingId:guid}/auto-send")]
    public async Task<IActionResult> SetAutoSend(Guid id, Guid mappingId, [FromBody] SetAutoSendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SetMappingAutoSendCommand(id, mappingId, request.Enabled), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/mappings/{mappingId:guid}/conversions")]
    public async Task<IActionResult> ReplaceConversions(Guid id, Guid mappingId, [FromBody] ReplaceConversionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new ReplaceMappingConversionsCommand(id, mappingId, request.Conversions ?? []), cancellationToken);
        return NoContent();
    }

    /// <summary>Sets the analyser-specific range; a body with no bound and no text (or none) clears it.</summary>
    [HttpPut("{id:guid}/mappings/{mappingId:guid}/range-override")]
    public async Task<IActionResult> SetRangeOverride(Guid id, Guid mappingId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RangeOverrideDto? request, CancellationToken cancellationToken)
    {
        await sender.Send(new SetMappingRangeOverrideCommand(id, mappingId, request), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/mappings/{mappingId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateMapping(Guid id, Guid mappingId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateMappingCommand(id, mappingId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/mappings/{mappingId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateMapping(Guid id, Guid mappingId, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateMappingCommand(id, mappingId), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/sample-codes")]
    public async Task<ActionResult<IReadOnlyList<SampleCodeItem>>> SampleCodes(Guid id, [FromQuery] bool? activeOnly, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAnalyserSampleCodesQuery(id, activeOnly), cancellationToken));

    [HttpPost("{id:guid}/sample-codes")]
    public async Task<ActionResult<CreatedResource>> AddSampleCode(Guid id, [FromBody] SaveSampleCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new AddAnalyserSampleCodeCommand(id, request.SampleTypeId, request.Code), cancellationToken);
        return Created($"/api/v1/integration/analysers/{id}/sample-codes/{created.Id}", created);
    }

    [HttpPut("{id:guid}/sample-codes/{sampleCodeId:guid}")]
    public async Task<IActionResult> ChangeSampleCode(Guid id, Guid sampleCodeId, [FromBody] SaveSampleCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new ChangeAnalyserSampleCodeCommand(id, sampleCodeId, request.Code), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/sample-codes/{sampleCodeId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateSampleCode(Guid id, Guid sampleCodeId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateAnalyserSampleCodeCommand(id, sampleCodeId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/sample-codes/{sampleCodeId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateSampleCode(Guid id, Guid sampleCodeId, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateAnalyserSampleCodeCommand(id, sampleCodeId), cancellationToken);
        return NoContent();
    }
}
