using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;

namespace Platform.WebApi.Controllers.PlatformAdmin;

/// <summary>Analyser models of the integration library (SRS SA-15).</summary>
[ApiController]
[Route("api/v1/platform/library/analyser-models")]
public sealed class PlatformAnalyserModelsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<AnalyserModelListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAnalyserModelsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] AnalyserModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new SaveAnalyserModelCommand(null, request.Vendor, request.Model, request.Discipline, request.Protocol, request.PhysicalLink, request.Modes, request.DriverVersion), cancellationToken);
        return Created($"/api/v1/platform/library/analyser-models/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AnalyserModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SaveAnalyserModelCommand(id, request.Vendor, request.Model, request.Discipline, request.Protocol, request.PhysicalLink, request.Modes, request.DriverVersion), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, false, cancellationToken);

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, true, cancellationToken);

    private async Task<IActionResult> Toggle(Guid id, bool active, CancellationToken cancellationToken)
    {
        await sender.Send(new SetLibraryItemActiveCommand(LibraryKind.AnalyserModel, id, active), cancellationToken);
        return NoContent();
    }
}

/// <summary>PCR kit templates of the integration library (SRS SA-16).</summary>
[ApiController]
[Route("api/v1/platform/library/pcr-kits")]
public sealed class PlatformPcrKitsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PcrKitListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPcrKitsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] PcrKitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new SavePcrKitCommand(null, request.Name, request.AnalyserModelId, request.LayoutJson, request.ThresholdsJson, request.VerdictRulesJson), cancellationToken);
        return Created($"/api/v1/platform/library/pcr-kits/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PcrKitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SavePcrKitCommand(id, request.Name, request.AnalyserModelId, request.LayoutJson, request.ThresholdsJson, request.VerdictRulesJson), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, false, cancellationToken);

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, true, cancellationToken);

    private async Task<IActionResult> Toggle(Guid id, bool active, CancellationToken cancellationToken)
    {
        await sender.Send(new SetLibraryItemActiveCommand(LibraryKind.PcrKit, id, active), cancellationToken);
        return NoContent();
    }
}

/// <summary>Code systems of the integration library (SRS SA-17), with the CSV import of their codes.</summary>
[ApiController]
[Route("api/v1/platform/library/code-systems")]
public sealed class PlatformCodeSystemsController(ISender sender) : ControllerBase
{
    /// <summary>The import body: the 20 MB file plus the multipart envelope.</summary>
    public const long ImportRequestLimit = CodeSystemCsv.MaxBytes + (1024 * 1024);

    [HttpGet]
    public async Task<ActionResult<Paged<CodeSystemListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCodeSystemsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("{id:guid}/items")]
    public async Task<ActionResult<Paged<CodeSystemItemView>>> Items(Guid id, [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCodeSystemItemsQuery(id, search, page, pageSize), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CodeSystemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new SaveCodeSystemCommand(null, request.Name, request.Version, request.Use), cancellationToken);
        return Created($"/api/v1/platform/library/code-systems/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CodeSystemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SaveCodeSystemCommand(id, request.Name, request.Version, request.Use), cancellationToken);
        return NoContent();
    }

    /// <summary>SA-17 "Import codes": multipart field <c>file</c>, CSV with a header (code, display), at most 20 MB; upsert by code.</summary>
    [HttpPost("{id:guid}/import")]
    [RequestSizeLimit(ImportRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportRequestLimit)]
    public async Task<ActionResult<CodeSystemImportResult>> Import(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var content = file.OpenReadStream();
        return Ok(await sender.Send(new ImportCodeSystemItemsCommand(id, content, file.Length), cancellationToken));
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, false, cancellationToken);

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, true, cancellationToken);

    private async Task<IActionResult> Toggle(Guid id, bool active, CancellationToken cancellationToken)
    {
        await sender.Send(new SetLibraryItemActiveCommand(LibraryKind.CodeSystem, id, active), cancellationToken);
        return NoContent();
    }
}

/// <summary>Medical-rule templates of the integration library (SRS SA-18).</summary>
[ApiController]
[Route("api/v1/platform/library/rule-templates")]
public sealed class PlatformRuleTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<RuleTemplateListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListRuleTemplatesQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] RuleTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new SaveRuleTemplateCommand(null, request.Name, request.ClinicalGroupName, request.Formula, request.Description), cancellationToken);
        return Created($"/api/v1/platform/library/rule-templates/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RuleTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new SaveRuleTemplateCommand(id, request.Name, request.ClinicalGroupName, request.Formula, request.Description), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, false, cancellationToken);

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) => await Toggle(id, true, cancellationToken);

    private async Task<IActionResult> Toggle(Guid id, bool active, CancellationToken cancellationToken)
    {
        await sender.Send(new SetLibraryItemActiveCommand(LibraryKind.RuleTemplate, id, active), cancellationToken);
        return NoContent();
    }
}
