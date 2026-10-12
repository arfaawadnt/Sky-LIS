using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Pricing.Commands;
using Platform.Application.Pricing.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;

namespace Platform.WebApi.Controllers.Pricing;

/// <summary>Base price lists and their lines (SRS CP-33). Dispatch only (D-04).</summary>
[ApiController]
[Route("api/v1/pricing/base-price-lists")]
public sealed class BasePriceListsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<BasePriceListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] int? year,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListBasePriceListsQuery(search, year, isActive, page, pageSize, sort, direction), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BasePriceListDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBasePriceListQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] CreateBasePriceListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreateBasePriceListCommand(request.Code, request.NameEn, request.NameAr, request.Year), cancellationToken);
        return Created($"/api/v1/pricing/base-price-lists/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBasePriceListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdateBasePriceListCommand(id, request.NameEn, request.NameAr, request.Year), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/lines")]
    public async Task<ActionResult<IReadOnlyList<PriceLineItem>>> Lines(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPriceLinesQuery(id), cancellationToken));

    /// <summary>L3: adds or updates the given lines (prices change contracts only on Apply, BR-07).</summary>
    [HttpPut("{id:guid}/lines")]
    public async Task<ActionResult<UpsertPriceLinesResult>> UpsertLines(Guid id, [FromBody] UpsertPriceLinesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new UpsertPriceLinesCommand(id, request.Lines ?? []), cancellationToken));
    }

    /// <summary>L4: copies this list into a new (inactive) list with × factor or + amount and a rounding step.</summary>
    [HttpPost("{id:guid}/copy")]
    public async Task<ActionResult<CreatedResource>> Copy(Guid id, [FromBody] CopyBasePriceListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CopyBasePriceListCommand(id, request.Code, request.NameEn, request.NameAr, request.Year, request.Operation, request.Value, request.RoundingStep), cancellationToken);
        return Created($"/api/v1/pricing/base-price-lists/{created.Id}", created);
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ActivateBasePriceListCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateBasePriceListCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>L6 (CP-33 "Apply"): re-prices every tier whose formula references this list.</summary>
    [HttpPost("{id:guid}/apply")]
    public async Task<ActionResult<ApplyBasePriceListResult>> Apply(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ApplyBasePriceListCommand(id), cancellationToken));
}
