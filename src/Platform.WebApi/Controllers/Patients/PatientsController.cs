using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Patients.Commands;
using Platform.Application.Patients.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Domain.Patients;

namespace Platform.WebApi.Controllers.Patients;

/// <summary>
/// Patient files and documents (SRS CP-11; intake LIS-INT-PAT-001). Dispatch only: the policy on each request decides
/// who may call it (D-04), the aggregate decides the rest. Documents are never deleted (BR-15): a deactivation endpoint
/// replaces DELETE.
/// </summary>
[ApiController]
[Route("api/v1/patients")]
public sealed class PatientsController(ISender sender) : ControllerBase
{
    /// <summary>The multipart body may carry the 10 MB document plus the form overhead; the handler decides the exact limit (PAT-041).</summary>
    private const long MaxUploadRequestBytes = PatientDocumentFiles.MaxSizeBytes + (64 * 1024);

    [HttpGet]
    public async Task<ActionResult<Paged<PatientListItem>>> List(
        [FromQuery] string? q,
        [FromQuery] string? gender,
        [FromQuery] bool? vip,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPatientsQuery(q, gender, vip, status, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? q, [FromQuery] string? gender, [FromQuery] bool? vip, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportPatientsQuery(q, gender, vip, status), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("similar")]
    public async Task<ActionResult<SimilarPatientsResponse>> Similar([FromQuery] string? name, [FromQuery] string? mobile, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new FindSimilarPatientsQuery(name, mobile), cancellationToken));

    [HttpGet("form-settings")]
    public async Task<ActionResult<PatientFormSettings>> FormSettings(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPatientFormSettingsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PatientDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPatientQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedPatient>> Register([FromBody] RegisterPatientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(
            new RegisterPatientCommand(
                request.Title, request.FullName, request.NameAr, request.Gender, request.DateOfBirth, request.Age, request.PatientConditionId, request.Mobile,
                request.NationalId, request.Nationality, request.Email, request.GovernorateId, request.City, request.Address, request.Comments, request.IsVip),
            cancellationToken);
        return Created($"/api/v1/patients/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePatientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(
            new UpdatePatientCommand(
                id, request.Title, request.FullName, request.NameAr, request.Gender, request.DateOfBirth, request.Age, request.PatientConditionId, request.Mobile,
                request.NationalId, request.Nationality, request.Email, request.GovernorateId, request.City, request.Address, request.Comments, request.IsVip, request.RowVersion),
            cancellationToken);
        return NoContent();
    }

    /// <summary>T3: the route's file survives; <see cref="MergePatientsRequest.MergedPatientId"/> becomes Merged.</summary>
    [HttpPost("{id:guid}/merge")]
    public async Task<IActionResult> Merge(Guid id, [FromBody] MergePatientsRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new MergePatientsCommand(id, body.MergedPatientId, body.Reason, body.ConfirmGenderMismatch), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<PatientDocumentItem>>> Documents(Guid id, [FromQuery] bool? includeInactive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPatientDocumentsQuery(id, includeInactive ?? true), cancellationToken));

    /// <summary>T4: <c>multipart/form-data</c> with <c>file</c> (pdf/jpg/png ≤ 10 MB) and an optional <c>note</c>.</summary>
    [HttpPost("{id:guid}/documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<ActionResult<CreatedResource>> Upload(Guid id, IFormFile? file, [FromForm] string? note, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream() ?? Stream.Null;
        var created = await sender.Send(new AttachPatientDocumentCommand(id, file?.FileName, content, note), cancellationToken);
        return Created($"/api/v1/patients/documents/{created.Id}", created);
    }

    /// <summary>The content with its detected type, as an attachment named after the uploaded file.</summary>
    [HttpGet("documents/{documentId:guid}")]
    public async Task<IActionResult> Download(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await sender.Send(new GetPatientDocumentContentQuery(documentId), cancellationToken);
        return File(document.Content, document.ContentType, document.FileName);
    }

    /// <summary>T5 (BR-15): the document stays on record with the reason.</summary>
    [HttpPost("documents/{documentId:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid documentId, [FromBody] DeactivatePatientDocumentRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new DeactivatePatientDocumentCommand(documentId, body.Reason), cancellationToken);
        return NoContent();
    }
}
