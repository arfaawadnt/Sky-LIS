using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Platform.Application.Results;
using Platform.Application.Results.Commands;
using Platform.Application.Results.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Results;

namespace Platform.WebApi.Controllers.Results;

/// <summary>
/// The medical track of results (SRS CP-18 Worklist, CP-19 Results by accession; WF-06): the case of a visit with its
/// report units, entry, review, verification, undo, amendment, delta acknowledgement, comments and notes, logs and
/// previous results, the worklist and the documents. Dispatch only: the policy on each request decides who may call it
/// (D-04); the Result aggregate decides the rest with the user's result rights; reasons travel in typed bodies (pitfall P-16).
/// </summary>
[ApiController]
[Route("api/v1/results")]
public sealed class ResultsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ResultCaseView>> GetCase([FromQuery] string? accession, [FromQuery] Guid? visitId, [FromQuery] string? sampleNumber, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetResultCaseQuery(accession, visitId, sampleNumber), cancellationToken));

    [HttpGet("visits")]
    public async Task<ActionResult<Paged<ResultVisitListItem>>> SearchVisits(
        [FromQuery] string? accession,
        [FromQuery] string? patientNumber,
        [FromQuery] string? patientName,
        [FromQuery] string? status,
        [FromQuery] string? sampleStatus,
        [FromQuery] Guid? analyserId,
        [FromQuery] bool notPrinted,
        [FromQuery] bool notVerified,
        [FromQuery] bool delta,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool branchOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SearchResultVisitsQuery(accession, patientNumber, patientName, status, sampleStatus, analyserId, notPrinted, notVerified, delta, from, to, branchOnly, page, pageSize), cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Enter(Guid id, [FromBody] EnterResultValueRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new EnterResultValueCommand(id, body.NumericValue, body.TextValue, body.Paragraphs, body.Culture), cancellationToken);
        return NoContent();
    }

    [HttpPut("values")]
    public async Task<ActionResult<BulkResultOutcome>> SaveValues([FromBody] SaveResultValuesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new SaveResultValuesCommand(body.Values ?? []), cancellationToken));
    }

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReviewResultCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new VerifyResultCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("review")]
    public async Task<ActionResult<BulkResultOutcome>> ReviewMany([FromBody] ResultIdsRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new ReviewResultsCommand(body.ResultIds ?? []), cancellationToken));
    }

    [HttpPost("verify")]
    public async Task<ActionResult<BulkResultOutcome>> VerifyMany([FromBody] ResultIdsRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new VerifyResultsCommand(body.ResultIds ?? []), cancellationToken));
    }

    [HttpPost("{id:guid}/undo")]
    public async Task<IActionResult> Undo(Guid id, [FromBody] UndoResultRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new UndoResultCommand(id, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/amend")]
    public async Task<IActionResult> Amend(Guid id, [FromBody] AmendResultRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new AmendResultCommand(id, body.NumericValue, body.TextValue, body.Paragraphs, body.Culture, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/acknowledge-delta")]
    public async Task<IActionResult> AcknowledgeDelta(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AcknowledgeDeltaRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new AcknowledgeDeltaCommand(id, body?.Comment), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/comment")]
    public async Task<IActionResult> SetComment(Guid id, [FromBody] ResultTextRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new SetResultCommentCommand(id, body.Text), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/note")]
    public async Task<IActionResult> SetNote(Guid id, [FromBody] ResultTextRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new SetResultNoteCommand(id, body.Text), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/log")]
    public async Task<ActionResult<IReadOnlyList<ResultLogItem>>> Log(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetResultLogQuery(id), cancellationToken));

    [HttpGet("{id:guid}/previous")]
    public async Task<ActionResult<IReadOnlyList<PreviousResultItem>>> Previous(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPreviousResultsQuery(id), cancellationToken));

    [HttpGet("worklist")]
    public async Task<ActionResult<WorklistPage>> Worklist(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? clinicalGroupId,
        [FromQuery] Guid? testId,
        [FromQuery] Guid? analyserId,
        [FromQuery] string? sampleStatus,
        [FromQuery] Guid? payerId,
        [FromQuery(Name = "status")] string[]? statuses,
        [FromQuery] bool delayedOnly,
        [FromQuery] string? search,
        [FromQuery] bool branchOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetWorklistQuery(from, to, clinicalGroupId, testId, analyserId, sampleStatus, payerId, statuses, delayedOnly, search, branchOnly, page, pageSize), cancellationToken));

    [HttpGet("worklist/documents/worklist")]
    public async Task<ActionResult<WorklistDocument>> WorklistBenchList(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? clinicalGroupId,
        [FromQuery] Guid? testId,
        [FromQuery] Guid? analyserId,
        [FromQuery] string? sampleStatus,
        [FromQuery] Guid? payerId,
        [FromQuery(Name = "status")] string[]? statuses,
        [FromQuery] bool delayedOnly,
        [FromQuery] string? search,
        [FromQuery] bool branchOnly,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetWorklistDocumentQuery(new GetWorklistQuery(from, to, clinicalGroupId, testId, analyserId, sampleStatus, payerId, statuses, delayedOnly, search, branchOnly, null, null)), cancellationToken));

    [HttpGet("report-units/{key}/documents/report")]
    public async Task<ActionResult<LaboratoryReportDocument>> Report(string key, [FromQuery] bool preview, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetReportDocumentQuery(key, preview), cancellationToken));

    [HttpPost("report-units/{key}/print")]
    public async Task<IActionResult> Print(string key, CancellationToken cancellationToken)
    {
        await sender.Send(new PrintReportUnitCommand(key), cancellationToken);
        return NoContent();
    }

    [HttpGet("lookups/comment-templates")]
    public async Task<ActionResult<IReadOnlyList<CommentTemplateOption>>> CommentTemplates([FromQuery] string? search, [FromQuery] Guid? clinicalGroupId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetCommentTemplatesQuery(search, clinicalGroupId), cancellationToken));

    [HttpGet("lookups/microbiology")]
    public async Task<ActionResult<MicrobiologyLookups>> Microbiology(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMicrobiologyLookupsQuery(), cancellationToken));

    [HttpGet("lookups/clinical-groups")]
    public async Task<ActionResult<IReadOnlyList<LookupOption>>> ClinicalGroups(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetResultClinicalGroupsQuery(), cancellationToken));

    [HttpGet("lookups/tests")]
    public async Task<ActionResult<IReadOnlyList<LookupOption>>> Tests([FromQuery] Guid? clinicalGroupId, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetResultTestsQuery(clinicalGroupId, search), cancellationToken));

    [HttpGet("lookups/payers")]
    public async Task<ActionResult<IReadOnlyList<LookupOption>>> Payers(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetResultPayersQuery(), cancellationToken));

    [HttpGet("lookups/analysers")]
    public async Task<ActionResult<IReadOnlyList<AnalyserOption>>> Analysers(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetResultAnalysersQuery(), cancellationToken));
}
