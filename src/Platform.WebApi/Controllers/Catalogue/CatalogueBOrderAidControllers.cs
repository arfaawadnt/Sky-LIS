using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Catalogue;

// Order aids of Catalogue B (SRS CP-46, CP-49, CP-51, CP-52). Dispatch only: the policy on each request decides who
// may call it (D-04); codes are immutable, so updates carry none; deactivation replaces deletion (BR-15).

[ApiController]
[Route("api/v1/catalogue/preparations")]
public sealed class PreparationsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PreparationListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string? answerType, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPreparationsQuery(search, isActive, answerType, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PreparationListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPreparationQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string? answerType, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportPreparationsQuery(search, isActive, answerType), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreatePreparationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreatePreparationCommand(request.Code, request.QuestionEn, request.QuestionAr, request.AnswerType, request.Options, request.Gender, request.ServiceIds), cancellationToken);
        return Created($"/api/v1/catalogue/preparations/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePreparationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdatePreparationCommand(id, request.QuestionEn, request.QuestionAr, request.AnswerType, request.Options, request.Gender, request.ServiceIds), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Preparations, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Preparations, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/diagnoses")]
public sealed class DiagnosesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<DiagnosisListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListDiagnosesQuery(search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DiagnosisListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDiagnosisQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportDiagnosesQuery(search, isActive), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateDiagnosisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateDiagnosisCommand(request.IcdCode, request.NameEn, request.NameAr, request.ServiceIds), cancellationToken);
        return Created($"/api/v1/catalogue/diagnoses/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDiagnosisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateDiagnosisCommand(id, request.NameEn, request.NameAr, request.ServiceIds), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Diagnoses, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Diagnoses, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/shortcut-sets")]
public sealed class ShortcutSetsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<ShortcutSetListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListShortcutSetsQuery(search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ShortcutSetListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetShortcutSetQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportShortcutSetsQuery(search, isActive), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateShortcutSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateShortcutSetCommand(request.Code, request.NameEn, request.NameAr, request.ServiceIds), cancellationToken);
        return Created($"/api/v1/catalogue/shortcut-sets/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShortcutSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateShortcutSetCommand(id, request.NameEn, request.NameAr, request.ServiceIds), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.ShortcutSets, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.ShortcutSets, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/test-rules")]
public sealed class TestRulesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<TestRuleListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? clinicalGroupId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListTestRulesQuery(search, isActive, clinicalGroupId, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TestRuleListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTestRuleQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? clinicalGroupId, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportTestRulesQuery(search, isActive, clinicalGroupId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>"Check rule" (T5): errors and warnings of a candidate rule; nothing is saved.</summary>
    [HttpPost("validate")]
    public async Task<ActionResult<TestRuleValidationResult>> Validate([FromBody] ValidateTestRuleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new ValidateTestRuleQuery(request.RuleId, request.TriggerServiceIds, request.AddedServiceIds), cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] TestRuleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateTestRuleCommand(request.NameEn, request.NameAr, request.ClinicalGroupId, request.TriggerServiceIds, request.AddedServiceIds), cancellationToken);
        return Created($"/api/v1/catalogue/test-rules/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TestRuleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateTestRuleCommand(id, request.NameEn, request.NameAr, request.ClinicalGroupId, request.TriggerServiceIds, request.AddedServiceIds), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.TestRules, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.TestRules, id, true), cancellationToken);
        return NoContent();
    }
}
