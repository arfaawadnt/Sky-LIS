using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Catalogue;

// Catalogue half A, simple lists (SRS CP-39, CP-43, CP-44, CP-45). Dispatch only: the policy on each request decides
// who may call it (D-04); reasons and rules live in the aggregates.

/// <summary>Clinical groups (CP-39).</summary>
[ApiController]
[Route("api/v1/catalogue/clinical-groups")]
public sealed class ClinicalGroupsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<ClinicalGroupListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListClinicalGroupsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<ClinicalGroupListItem>>> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportClinicalGroupsQuery(search, isActive), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateClinicalGroupRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(new CreateClinicalGroupCommand(body.Code, body.UnitCode, body.NameEn, body.NameAr, body.ReportName, body.ReportTemplateId, body.DisplayOrder), cancellationToken);
        return Created($"/api/v1/catalogue/clinical-groups/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClinicalGroupRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new UpdateClinicalGroupCommand(id, body.UnitCode, body.NameEn, body.NameAr, body.ReportName, body.ReportTemplateId, body.DisplayOrder), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetClinicalGroupActiveCommand(id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetClinicalGroupActiveCommand(id, true), cancellationToken);
        return NoContent();
    }
}

/// <summary>Sample types and their conditions (CP-43).</summary>
[ApiController]
[Route("api/v1/catalogue/sample-types")]
public sealed class SampleTypesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SampleTypeListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSampleTypesQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<SampleTypeListItem>>> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportSampleTypesQuery(search, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SampleTypeDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSampleTypeQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateSampleTypeRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(new CreateSampleTypeCommand(body.Code, body.ShortName, body.NameEn, body.NameAr, body.Conditions), cancellationToken);
        return Created($"/api/v1/catalogue/sample-types/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSampleTypeRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new UpdateSampleTypeCommand(id, body.ShortName, body.NameEn, body.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/conditions")]
    public async Task<IActionResult> SaveConditions(Guid id, [FromBody] SaveSampleConditionsRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new SaveSampleConditionsCommand(id, body.Conditions), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetSampleTypeActiveCommand(id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetSampleTypeActiveCommand(id, true), cancellationToken);
        return NoContent();
    }
}

/// <summary>Units of measurement (CP-44).</summary>
[ApiController]
[Route("api/v1/catalogue/units")]
public sealed class MeasurementUnitsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<MeasurementUnitListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListMeasurementUnitsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<MeasurementUnitListItem>>> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportMeasurementUnitsQuery(search, isActive), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] MeasurementUnitRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(new CreateMeasurementUnitCommand(body.Name), cancellationToken);
        return Created($"/api/v1/catalogue/units/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] MeasurementUnitRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new RenameMeasurementUnitCommand(id, body.Name), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetMeasurementUnitActiveCommand(id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetMeasurementUnitActiveCommand(id, true), cancellationToken);
        return NoContent();
    }
}

/// <summary>Patient conditions (CP-45).</summary>
[ApiController]
[Route("api/v1/catalogue/patient-conditions")]
public sealed class PatientConditionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PatientConditionListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPatientConditionsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<PatientConditionListItem>>> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportPatientConditionsQuery(search, isActive), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] PatientConditionRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(new CreatePatientConditionCommand(body.NameEn, body.NameAr), cancellationToken);
        return Created($"/api/v1/catalogue/patient-conditions/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PatientConditionRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new RenamePatientConditionCommand(id, body.NameEn, body.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetPatientConditionActiveCommand(id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetPatientConditionActiveCommand(id, true), cancellationToken);
        return NoContent();
    }
}
