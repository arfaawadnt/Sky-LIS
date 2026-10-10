using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Catalogue;

// Set-up lists of Catalogue B (SRS CP-47, CP-48, CP-50, CP-53, CP-54). Dispatch only (D-04).

[ApiController]
[Route("api/v1/catalogue/comment-templates")]
public sealed class CommentTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<CommentTemplateListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? clinicalGroupId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCommentTemplatesQuery(search, isActive, clinicalGroupId, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CommentTemplateListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetCommentTemplateQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? clinicalGroupId, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportCommentTemplatesQuery(search, isActive, clinicalGroupId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateCommentTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateCommentTemplateCommand(request.ShortName, request.TextEn, request.TextAr, request.ClinicalGroupId), cancellationToken);
        return Created($"/api/v1/catalogue/comment-templates/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommentTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateCommentTemplateCommand(id, request.TextEn, request.TextAr, request.ClinicalGroupId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.CommentTemplates, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.CommentTemplates, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/doctors")]
public sealed class DoctorsController(ISender sender) : ControllerBase
{
    /// <summary><paramref name="branchId"/>: the doctors offered in that branch (registration picker).</summary>
    [HttpGet]
    public async Task<ActionResult<Paged<DoctorListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? branchId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListDoctorsQuery(search, isActive, branchId, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DoctorListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDoctorQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? branchId, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportDoctorsQuery(search, isActive, branchId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateDoctorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateDoctorCommand(request.Code, request.NameEn, request.NameAr, request.Specialty, request.Occupation, request.BranchIds, request.SharePercent, request.Phone), cancellationToken);
        return Created($"/api/v1/catalogue/doctors/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDoctorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateDoctorCommand(id, request.NameEn, request.NameAr, request.Specialty, request.Occupation, request.BranchIds, request.SharePercent, request.Phone), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Doctors, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Doctors, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/sending-labs")]
public sealed class SendingLabsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SendingLabListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? payerId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSendingLabsQuery(search, isActive, payerId, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SendingLabListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSendingLabQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? payerId, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportSendingLabsQuery(search, isActive, payerId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] SendingLabRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateSendingLabCommand(request.NameEn, request.NameAr, request.PayerId, request.ContractTierId, request.CourierEn, request.CourierAr, request.Phone, request.DoctorId), cancellationToken);
        return Created($"/api/v1/catalogue/sending-labs/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SendingLabRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateSendingLabCommand(id, request.NameEn, request.NameAr, request.PayerId, request.ContractTierId, request.CourierEn, request.CourierAr, request.Phone, request.DoctorId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.SendingLabs, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.SendingLabs, id, true), cancellationToken);
        return NoContent();
    }
}

/// <summary>Reason lists (CP-53). Other modules read the active reasons of a kind with <c>GET ?kind=Cancellation&amp;isActive=true</c>.</summary>
[ApiController]
[Route("api/v1/catalogue/reasons")]
public sealed class ReasonsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<ReasonListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string? kind, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListReasonsQuery(search, isActive, kind, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReasonListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetReasonQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string? kind, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportReasonsQuery(search, isActive, kind), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateReasonCommand(request.Kind, request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/catalogue/reasons/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateReasonCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Reasons, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Reasons, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/report-templates")]
public sealed class ReportTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<ReportTemplateListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string? type, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListReportTemplatesQuery(search, isActive, type, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportTemplateListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetReportTemplateQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string? type, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportReportTemplatesQuery(search, isActive, type), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] ReportTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(
            new CreateReportTemplateCommand(request.NameEn, request.NameAr, request.Type, request.LogoFileId, request.SignatureFileId, request.ShowQr, request.Language, request.HeaderTextEn, request.HeaderTextAr, request.FooterTextEn, request.FooterTextAr),
            cancellationToken);
        return Created($"/api/v1/catalogue/report-templates/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ReportTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(
            new UpdateReportTemplateCommand(id, request.NameEn, request.NameAr, request.Type, request.LogoFileId, request.SignatureFileId, request.ShowQr, request.Language, request.HeaderTextEn, request.HeaderTextAr, request.FooterTextEn, request.FooterTextAr),
            cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.ReportTemplates, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.ReportTemplates, id, true), cancellationToken);
        return NoContent();
    }
}
