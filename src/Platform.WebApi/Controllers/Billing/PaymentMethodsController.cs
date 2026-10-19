using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Billing.Commands;
using Platform.Application.Billing.Queries;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Billing;

/// <summary>
/// Payment methods set-up list (Sky LIS CP-30b, change request 2026-10-19 P-C10). Dispatch only: the policy on each
/// request decides who may call it (D-04): <c>billing.view</c> reads (the options feed the cash desk and the registration
/// page), <c>billing.manage</c> writes.
/// </summary>
[ApiController]
[Route("api/v1/billing/payment-methods")]
public sealed class BillingPaymentMethodsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PaymentMethodListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPaymentMethodsQuery(search, isActive, page, pageSize, sort, direction), cancellationToken));

    /// <summary>The active methods in pick-list order (id, code, names, kind, whether a reference is required).</summary>
    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodOption>>> Options(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPaymentMethodOptionsQuery(), cancellationToken));

    /// <summary>CSV of the list under the grid's filter (<c>billing.export</c>).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportPaymentMethodsQuery(search, isActive), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentMethodListItem>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPaymentMethodQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedResource>> Create([FromBody] PaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new CreatePaymentMethodCommand(request.Code, request.NameEn, request.NameAr, request.Kind, request.RequiresReference, request.Sequence), cancellationToken);
        return Created($"/api/v1/billing/payment-methods/{created.Id}", created);
    }

    /// <summary>Adds the default methods the tenant lacks (idempotent; the set-up page calls it when its list is empty).</summary>
    [HttpPost("defaults")]
    public async Task<ActionResult<PaymentMethodDefaultsResult>> Defaults(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new EnsureDefaultPaymentMethodsCommand(), cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sender.Send(new UpdatePaymentMethodCommand(id, request.NameEn, request.NameAr, request.Kind, request.RequiresReference, request.Sequence), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivatePaymentMethodCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivatePaymentMethodCommand(id), cancellationToken);
        return NoContent();
    }
}
