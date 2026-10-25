using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Billing.Commands;
using Platform.Application.Billing.Queries;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;

namespace Platform.WebApi.Controllers.Billing;

// Billing (SRS CP-23, CP-24, CP-27, CP-30; intake LIS-INT-BIL-001). Dispatch only: the policy on each request decides
// who may call it (D-04); reasons travel in typed bodies or query parameters, never in headers (P-16). Cashier and
// branch of a movement are the session's (LIS-CR-004, 2026-10-20: no treasury, no device).

/// <summary>Payments (CP-23; BR-26: the registration page posts the captured payment here right after saving the visit).</summary>
[ApiController]
[Route("api/v1/billing/payments")]
public sealed class BillingPaymentsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<PaymentListItem>>> List(
        [FromQuery] Guid? visitId,
        [FromQuery] string? accession,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? cashierUserId,
        [FromQuery] string? method,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPaymentsQuery(visitId, accession, from, to, branchId, cashierUserId, method, status, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? visitId,
        [FromQuery] string? accession,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? cashierUserId,
        [FromQuery] string? method,
        [FromQuery] string? status,
        [FromQuery] string? timeZone,
        CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportPaymentsQuery(visitId, accession, from, to, branchId, cashierUserId, method, status, timeZone), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<PaymentCreated>> Record([FromBody] RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(new RecordPaymentCommand(request.VisitId, request.Amount, request.Method, request.Reference, request.MethodCode), cancellationToken);
        return Created($"/api/v1/billing/payments/{created.Id}", created);
    }

    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidPaymentRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new VoidPaymentCommand(id, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents/receipt")]
    public async Task<ActionResult<PaymentReceiptDocument>> Receipt(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPaymentReceiptQuery(id), cancellationToken));
}

/// <summary>Refunds (CP-24, BR-14).</summary>
[ApiController]
[Route("api/v1/billing/refunds")]
public sealed class BillingRefundsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<RefundListItem>>> List(
        [FromQuery] Guid? visitId,
        [FromQuery] string? accession,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? cashierUserId,
        [FromQuery] string? method,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListRefundsQuery(visitId, accession, from, to, branchId, cashierUserId, method, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? visitId,
        [FromQuery] string? accession,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? cashierUserId,
        [FromQuery] string? method,
        [FromQuery] string? timeZone,
        CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportRefundsQuery(visitId, accession, from, to, branchId, cashierUserId, method, timeZone), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<RefundCreated>> Record([FromBody] RecordRefundRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(
            new RecordRefundCommand(request.VisitId, request.Amount, request.Method, request.Reference, request.ReasonId, request.Note, request.MethodCode),
            cancellationToken);
        return Created($"/api/v1/billing/refunds/{created.Id}", created);
    }

    [HttpGet("{id:guid}/documents/receipt")]
    public async Task<ActionResult<RefundReceiptDocument>> Receipt(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetRefundReceiptQuery(id), cancellationToken));
}

/// <summary>Visit accounts and invoices (CP-23).</summary>
[ApiController]
[Route("api/v1/billing/visits")]
public sealed class BillingVisitsController(ISender sender) : ControllerBase
{
    [HttpGet("{visitId:guid}/account")]
    public async Task<ActionResult<VisitAccountView>> Account(Guid visitId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetVisitAccountQuery(visitId, null), cancellationToken));

    [HttpGet("by-accession/{accession}/account")]
    public async Task<ActionResult<VisitAccountView>> AccountByAccession(string accession, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetVisitAccountQuery(null, accession), cancellationToken));

    [HttpGet("{visitId:guid}/documents/invoice")]
    public async Task<ActionResult<InvoiceDocument>> Invoice(Guid visitId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetInvoiceQuery(visitId), cancellationToken));
}

/// <summary>Batch payment (CP-27): the account of a batch and pay-all.</summary>
[ApiController]
[Route("api/v1/billing/batches")]
public sealed class BillingBatchesController(ISender sender) : ControllerBase
{
    [HttpGet("{batchId:guid}/account")]
    public async Task<ActionResult<BatchAccountView>> Account(Guid batchId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBatchAccountQuery(batchId, null), cancellationToken));

    [HttpGet("by-number/{number}/account")]
    public async Task<ActionResult<BatchAccountView>> AccountByNumber(string number, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBatchAccountQuery(null, number), cancellationToken));

    [HttpPost("{batchId:guid}/pay-all")]
    public async Task<ActionResult<BatchPaymentResult>> PayAll(Guid batchId, [FromBody] PayBatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await sender.Send(new PayBatchCommand(batchId, request.Amount, request.Method, request.Reference, request.MethodCode), cancellationToken));
    }
}

/// <summary>Cash closing (CP-30, BR-16).</summary>
[ApiController]
[Route("api/v1/billing/cash-closings")]
public sealed class BillingCashClosingsController(ISender sender) : ControllerBase
{
    /// <summary>The drawer of the session branch for one cashier (<c>cashierUserId</c>) or all of them (omitted).</summary>
    [HttpGet("preview")]
    public async Task<ActionResult<CashClosingPreview>> Preview(
        [FromQuery] Guid? cashierUserId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new PreviewCashClosingQuery(cashierUserId, from, to), cancellationToken));

    [HttpGet("cashiers")]
    public async Task<ActionResult<IReadOnlyList<DrawerCashier>>> Cashiers([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListDrawerCashiersQuery(from, to), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CashClosingCreated>> Close([FromBody] CloseCashDrawerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(
            new CloseCashDrawerCommand(request.CashierUserId, request.PeriodFromUtc, request.PeriodToUtc, request.CountedCash, request.Note),
            cancellationToken);
        return Created($"/api/v1/billing/cash-closings/{created.Id}", created);
    }

    [HttpGet]
    public async Task<ActionResult<Paged<CashClosingListItem>>> List(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? cashierUserId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCashClosingsQuery(branchId, cashierUserId, from, to, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] Guid? branchId, [FromQuery] Guid? cashierUserId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? timeZone, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportCashClosingsQuery(branchId, cashierUserId, from, to, timeZone), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{id:guid}/documents/sheet")]
    public async Task<ActionResult<CashClosingSheetDocument>> Sheet(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetCashClosingSheetQuery(id), cancellationToken));
}

/// <summary>Cash entries of the cash ledger (CP-70, change request LIS-CR-011): money out of or into the drawer without a visit.</summary>
[ApiController]
[Route("api/v1/billing/cash-entries")]
public sealed class BillingCashEntriesController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CashEntryCreated>> Post([FromBody] PostCashEntryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await sender.Send(
            new PostCashEntryCommand(request.Kind, request.Category, request.Amount, request.Reason, request.Method, request.MethodCode, request.Reference, request.Note),
            cancellationToken);
        return Created($"/api/v1/billing/cash-entries/{created.Id}", created);
    }

    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidCashEntryRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new VoidCashEntryCommand(id, body.Reason), cancellationToken);
        return NoContent();
    }
}

/// <summary>The cash desk's own reads: print parameters and the collections report (the treasury reads were removed by LIS-CR-004).</summary>
[ApiController]
[Route("api/v1/billing")]
public sealed class BillingDeskController(ISender sender) : ControllerBase
{
    /// <summary>The cash desk's print parameters (<c>autoPrintReceipt</c>) under <c>billing.view</c> (UAT A-05).</summary>
    [HttpGet("print-settings")]
    public async Task<ActionResult<BillingPrintSettings>> PrintSettings(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBillingPrintSettingsQuery(), cancellationToken));

    /// <summary>
    /// The collections report printed from the Payments register (change request 2026-10-13, item 2): whole
    /// laboratory days <c>from</c>…<c>to</c> (default today), <c>scope</c> User | Branch (BIL-044 without the right),
    /// <c>groupBy</c> Branch | User | Method; a period before the member's report days limit is refused with BIL-045.
    /// </summary>
    /// <summary>
    /// The cash ledger of the session branch (CP-70, change request LIS-CR-011): whole laboratory days <c>from</c>…<c>to</c> (default
    /// today), <c>scope</c> User (own drawer, default) | Branch (one <c>cashierUserId</c> or every cashier; BIL-044 without the right).
    /// </summary>
    [HttpGet("cash-ledger")]
    public async Task<ActionResult<CashLedgerView>> CashLedger(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? scope,
        [FromQuery] Guid? cashierUserId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new CashLedgerQuery(from, to, scope, cashierUserId), cancellationToken));

    [HttpGet("collections-report")]
    public async Task<ActionResult<CollectionsReportDocument>> CollectionsReport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? scope,
        [FromQuery] string? groupBy,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new CollectionsReportQuery(from, to, scope, groupBy), cancellationToken));
}
