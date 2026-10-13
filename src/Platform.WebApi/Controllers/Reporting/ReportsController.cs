using MediatR;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Reporting.Queries;
using Platform.Contracts.Reporting;

namespace Platform.WebApi.Controllers.Reporting;

/// <summary>Reports of the laboratory (change request 2026-10-13 item 3): the work amount report and its CSV export.</summary>
[ApiController]
[Route("api/v1/reports")]
public sealed class ReportsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// The income of the visits registered in the period (laboratory days <c>from</c>…<c>to</c>, default today), cash and
    /// credit, cancelled services excluded, grouped by <c>groupBy</c> (Branch | User | Payer | ContractTier | Doctor | Day |
    /// ClinicalGroup | Service; default Day); <c>branchId</c> null = the session branch. REP-001 before the member's days
    /// limit; REP-003 for a branch the member may not report on. The same document prints (A4 landscape).
    /// </summary>
    [HttpGet("work-amount")]
    public async Task<ActionResult<WorkAmountReport>> WorkAmount(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? groupBy,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new WorkAmountReportQuery(from, to, groupBy, branchId), cancellationToken));

    /// <summary>The same report as a CSV file (<c>reporting.export</c>).</summary>
    [HttpGet("work-amount/export")]
    public async Task<IActionResult> ExportWorkAmount(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? groupBy,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportWorkAmountReportQuery(from, to, groupBy, branchId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
