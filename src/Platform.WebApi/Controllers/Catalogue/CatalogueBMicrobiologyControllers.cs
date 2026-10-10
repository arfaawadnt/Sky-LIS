using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Catalogue;

// Microbiology masters of Catalogue B (SRS CP-56, SA-14). Dispatch only (D-04).

[ApiController]
[Route("api/v1/catalogue/microbiology/drug-types")]
public sealed class DrugTypesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<DrugTypeListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListDrugTypesQuery(search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DrugTypeListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDrugTypeQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportDrugTypesQuery(search, isActive), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] DrugTypeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateDrugTypeCommand(request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/catalogue/microbiology/drug-types/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DrugTypeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateDrugTypeCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.DrugTypes, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.DrugTypes, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/microbiology/commercial-names")]
public sealed class CommercialNamesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<CommercialNameListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? drugTypeId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCommercialNamesQuery(search, isActive, drugTypeId, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CommercialNameListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetCommercialNameQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] Guid? drugTypeId, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportCommercialNamesQuery(search, isActive, drugTypeId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CommercialNameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateCommercialNameCommand(request.DrugTypeId, request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/catalogue/microbiology/commercial-names/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CommercialNameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateCommercialNameCommand(id, request.DrugTypeId, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.CommercialNames, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.CommercialNames, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/microbiology/antibiotics")]
public sealed class AntibioticsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<AntibioticListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListAntibioticsQuery(search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AntibioticListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAntibioticQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportAntibioticsQuery(search, isActive), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateAntibioticRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateAntibioticCommand(request.Code, request.NameEn, request.NameAr, request.Abbreviation, request.CommercialNameId), cancellationToken);
        return Created($"/api/v1/catalogue/microbiology/antibiotics/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAntibioticRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateAntibioticCommand(id, request.NameEn, request.NameAr, request.Abbreviation, request.CommercialNameId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Antibiotics, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Antibiotics, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/microbiology/organisms")]
public sealed class OrganismsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<OrganismListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListOrganismsQuery(search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganismListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetOrganismQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportOrganismsQuery(search, isActive), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateOrganismRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateOrganismCommand(request.Code, request.NameEn, request.NameAr), cancellationToken);
        return Created($"/api/v1/catalogue/microbiology/organisms/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrganismRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateOrganismCommand(id, request.NameEn, request.NameAr), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Organisms, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.Organisms, id, true), cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/catalogue/microbiology/sensitivity-values")]
public sealed class SensitivityValuesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<SensitivityValueListItem>>> List([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSensitivityValuesQuery(search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SensitivityValueListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSensitivityValueQuery(id), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportSensitivityValuesQuery(search, isActive), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateSensitivityValueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateSensitivityValueCommand(request.Code, request.NameEn, request.NameAr, request.DisplayOrder), cancellationToken);
        return Created($"/api/v1/catalogue/microbiology/sensitivity-values/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSensitivityValueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateSensitivityValueCommand(id, request.NameEn, request.NameAr, request.DisplayOrder), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.SensitivityValues, id, false), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SetCatalogueBRecordActiveCommand(CatalogueBList.SensitivityValues, id, true), cancellationToken);
        return NoContent();
    }
}
