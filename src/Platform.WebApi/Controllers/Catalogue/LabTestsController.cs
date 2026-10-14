using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Catalogue;

/// <summary>Tests (CP-41): general data and one endpoint per configuration (timing, range bands, list values, formula, paragraphs).</summary>
[ApiController]
[Route("api/v1/catalogue/tests")]
public sealed class LabTestsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<LabTestListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] Guid? clinicalGroupId,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListLabTestsQuery(search, type, clinicalGroupId, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<LabTestListItem>>> Export([FromQuery] string? search, [FromQuery] string? type, [FromQuery] Guid? clinicalGroupId, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportLabTestsQuery(search, type, clinicalGroupId, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LabTestDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetLabTestQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateLabTestRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(
            new CreateLabTestCommand(
                body.Code, body.NameEn, body.NameAr, body.ReportName, body.Loinc, body.Cpt, body.ClinicalGroupId, body.Type, body.SampleTypeId,
                body.SampleConditionId, body.UnitId, body.Gender, body.Methodology, body.Decimals, body.IsHiddenHelper, body.PrintReference, body.LabelCount, body.Prefix),
            cancellationToken);
        return Created($"/api/v1/catalogue/tests/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLabTestRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(
            new UpdateLabTestCommand(
                id, body.NameEn, body.NameAr, body.ReportName, body.Loinc, body.Cpt, body.ClinicalGroupId, body.Type, body.SampleTypeId,
                body.SampleConditionId, body.UnitId, body.Gender, body.Methodology, body.Decimals, body.IsHiddenHelper, body.PrintReference, body.LabelCount, body.Prefix),
            cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/timing")]
    public async Task<IActionResult> SetTiming(Guid id, [FromBody] LabTestTimingItem body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new SetLabTestTimingCommand(id, body.DurationHours, body.MinDurationHours, body.FrequencyCheckDays, body.FrequencyAction, body.DeltaPercent, body.DeltaWindowDays, body.TestDays), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/range-bands")]
    public async Task<IActionResult> ReplaceRangeBands(Guid id, [FromBody] ReplaceRangeBandsRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new ReplaceRangeBandsCommand(id, body.Bands), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/list-values")]
    public async Task<IActionResult> ReplaceListValues(Guid id, [FromBody] ReplaceListValuesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new ReplaceListValuesCommand(id, body.Values), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/formula")]
    public async Task<IActionResult> SetFormula(Guid id, [FromBody] SetFormulaRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new SetLabTestFormulaCommand(id, body.Expression), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/paragraphs")]
    public async Task<IActionResult> ReplaceParagraphs(Guid id, [FromBody] ReplaceParagraphsRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new ReplaceParagraphsCommand(id, body.Paragraphs), cancellationToken);
        return NoContent();
    }

    /// <summary>The services that carry the test (CP-41 "parent services"): the test joins the new ones and leaves the omitted ones.</summary>
    [HttpPut("{id:guid}/parent-services")]
    public async Task<IActionResult> SetParentServices(Guid id, [FromBody] SetParentServicesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new SetLabTestParentServicesCommand(id, body.ServiceIds), cancellationToken);
        return NoContent();
    }

    [HttpPost("formula/check")]
    public async Task<ActionResult<FormulaCheckResult>> CheckFormula([FromBody] CheckFormulaRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new CheckFormulaQuery(body.Expression, body.TestId), cancellationToken));
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetLabTestActiveCommand(id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetLabTestActiveCommand(id, true), cancellationToken);
        return NoContent();
    }
}
