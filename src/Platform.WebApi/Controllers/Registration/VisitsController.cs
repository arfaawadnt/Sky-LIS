using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Platform.Application.Registration.Commands;
using Platform.Application.Registration.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;

namespace Platform.WebApi.Controllers.Registration;

/// <summary>Visits (SRS CP-06, CP-10, CP-25, CP-26): one endpoint per transition of intake LIS-INT-REG-001; dispatch only (D-04).</summary>
[ApiController]
[Route("api/v1/registration/visits")]
public sealed class VisitsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<VisitListItem>>> Search(
        [FromQuery] string? accession,
        [FromQuery] string? patientNo,
        [FromQuery] string? name,
        [FromQuery] string? mobile,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? payerId,
        [FromQuery] string? status,
        [FromQuery] Guid? patientId,
        [FromQuery] Guid? batchId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SearchVisitsQuery(accession, patientNo, name, mobile, from, to, branchId, payerId, status, patientId, batchId, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<VisitListItem>>> Export(
        [FromQuery] string? accession,
        [FromQuery] string? patientNo,
        [FromQuery] string? name,
        [FromQuery] string? mobile,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? payerId,
        [FromQuery] string? status,
        [FromQuery] Guid? patientId,
        [FromQuery] Guid? batchId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportVisitsQuery(accession, patientNo, name, mobile, from, to, branchId, payerId, status, patientId, batchId), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<VisitRegisteredResponse>> Register([FromBody] RegisterVisitRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(
            new RegisterVisitCommand(
                body.PatientId,
                body.TreatmentNo,
                body.ReferringDoctorId,
                body.PayerId,
                body.ContractTierId,
                body.InsuranceId,
                body.DiagnosisId,
                body.BookingId,
                body.Urgent,
                body.Biohazard,
                body.CollectedOutside,
                body.Lines ?? [],
                body.PreparationAnswers,
                body.Discount,
                body.ConfirmFrequencyWarnings,
                body.UtcOffsetMinutes,
                body.Samples),
            cancellationToken);
        return Created($"/api/v1/registration/visits/{created.Id}", created);
    }

    /// <summary>The dry run (nothing saved): lines, tubes, totals, refusals, warnings and preparation questions.</summary>
    [HttpPost("check")]
    public async Task<ActionResult<OrderCheckResult>> Check([FromBody] CheckOrderRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new CheckOrderQuery(body.VisitId, body.PatientId, body.PayerId, body.ContractTierId, body.DiagnosisId, body.Lines ?? [], body.UtcOffsetMinutes), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VisitDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetVisitQuery(id), cancellationToken));

    [HttpPost("{id:guid}/lines")]
    public async Task<ActionResult<VisitLinesAddedResponse>> AddLines(Guid id, [FromBody] AddVisitLinesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new AddVisitLinesCommand(id, body.Lines ?? [], body.PreparationAnswers, body.ConfirmFrequencyWarnings), cancellationToken));
    }

    [HttpPost("{id:guid}/lines/release")]
    public async Task<IActionResult> Release(Guid id, [FromBody] ReleaseLinesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new ReleaseReservedLinesCommand(id, body.LineIds ?? []), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/lines/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelLinesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new CancelVisitLinesCommand(id, body.LineIds ?? [], body.ReasonId, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/details")]
    public async Task<IActionResult> UpdateDetails(Guid id, [FromBody] UpdateVisitDetailsRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new UpdateVisitDetailsCommand(id, body.TreatmentNo, body.ReferringDoctorId, body.InsuranceId, body.Urgent, body.Biohazard), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/discount")]
    public async Task<IActionResult> SetDiscount(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SetVisitDiscountRequest? body, CancellationToken cancellationToken)
    {
        await sender.Send(new SetVisitDiscountCommand(id, body?.Discount, body?.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/transfer/preview")]
    public async Task<ActionResult<TransferPreview>> PreviewTransfer(Guid id, [FromBody] TransferContractRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new PreviewContractTransferQuery(id, body.PayerId, body.ContractTierId), cancellationToken));
    }

    [HttpPost("{id:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferContractRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new TransferVisitContractCommand(id, body.PayerId, body.ContractTierId, body.ReasonId, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/lines/{lineId:guid}/shares")]
    public async Task<IActionResult> AdjustShares(Guid id, Guid lineId, [FromBody] AdjustLineSharesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new AdjustVisitLineSharesCommand(id, lineId, body.PatientShare, body.PayerShare, body.ReasonId, body.Reason), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents/labels")]
    public async Task<ActionResult<VisitLabelsDocument>> Labels(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetVisitLabelsQuery(id), cancellationToken));

    [HttpGet("{id:guid}/documents/job-order")]
    public async Task<ActionResult<JobOrderDocument>> JobOrder(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetJobOrderQuery(id), cancellationToken));
}

/// <summary>Order aids, price query, settings and the money registers of the module (CP-06, CP-12, CP-25, CP-26).</summary>
[ApiController]
[Route("api/v1/registration")]
public sealed class RegistrationLookupsController(ISender sender) : ControllerBase
{
    [HttpGet("preparations")]
    public async Task<ActionResult<IReadOnlyList<PreparationQuestionItem>>> Preparations([FromQuery] Guid[]? serviceIds, [FromQuery] string? gender, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPreparationsQuery(serviceIds ?? [], gender), cancellationToken));

    [HttpGet("service-entry")]
    public async Task<ActionResult<ServiceEntryResult>> ServiceEntry([FromQuery] string? text, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ResolveServiceEntryQuery(text ?? string.Empty), cancellationToken));

    [HttpGet("settings")]
    public async Task<ActionResult<RegistrationPageSettings>> Settings(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetRegistrationSettingsQuery(), cancellationToken));

    [HttpPost("quote")]
    public async Task<ActionResult<PriceQuoteDocument>> Quote([FromBody] PriceQuoteRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new PriceQuoteQuery(body.PayerId, body.ContractTierId, body.ServiceIds ?? [], body.Gender, body.UtcOffsetMinutes), cancellationToken));
    }

    [HttpGet("cancellations")]
    public async Task<ActionResult<Paged<CancelledLineItem>>> Cancellations([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? branchId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListCancelledLinesQuery(from, to, branchId, page, pageSize), cancellationToken));

    [HttpGet("cancellations/export")]
    public async Task<ActionResult<IReadOnlyList<CancelledLineItem>>> ExportCancellations([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? branchId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportCancelledLinesQuery(from, to, branchId), cancellationToken));

    [HttpGet("transfers")]
    public async Task<ActionResult<Paged<TransferLogItem>>> Transfers([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? kind, [FromQuery] Guid? visitId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListTransfersQuery(from, to, kind, visitId, page, pageSize), cancellationToken));

    [HttpGet("sending-labs")]
    public async Task<ActionResult<IReadOnlyList<SendingLabOption>>> SendingLabs(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSendingLabsQuery(), cancellationToken));
}

/// <summary>Home reservations (SRS CP-07, Release 2 page): the conversion is POST /visits with the booking id.</summary>
[ApiController]
[Route("api/v1/registration/bookings")]
public sealed class BookingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<BookingListItem>>> List([FromQuery] string? status, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? phlebotomistUserId, [FromQuery] string? search, [FromQuery] Guid? branchId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListBookingsQuery(status, from, to, phlebotomistUserId, search, branchId, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<BookingListItem>>> Export([FromQuery] string? status, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? phlebotomistUserId, [FromQuery] string? search, [FromQuery] Guid? branchId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ExportBookingsQuery(status, from, to, phlebotomistUserId, search, branchId), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBookingQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<BookingCreatedResponse>> Create([FromBody] BookingRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(new CreateBookingCommand(body), cancellationToken);
        return Created($"/api/v1/registration/bookings/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] BookingRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new UpdateBookingCommand(id, body), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelBookingRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sender.Send(new CancelBookingCommand(id, body.Reason), cancellationToken);
        return NoContent();
    }
}

/// <summary>Batch and lab-to-lab registration (SRS CP-08, CP-09, Release 2 pages).</summary>
[ApiController]
[Route("api/v1/registration/batches")]
public sealed class BatchesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<BatchListItem>>> List([FromQuery] string? kind, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? search, [FromQuery] Guid? branchId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListBatchesQuery(kind, from, to, search, branchId, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BatchDetail>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBatchQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<BatchRegisteredResponse>> Register([FromBody] RegisterBatchRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = await sender.Send(
            new RegisterBatchCommand(body.Kind, body.PayerId, body.ContractTierId, body.SendingLabId, body.ReferringDoctorId, body.GroupName, body.Notes, body.ServiceIds, body.Members ?? [], body.ConfirmFrequencyWarnings, body.UtcOffsetMinutes),
            cancellationToken);
        return Created($"/api/v1/registration/batches/{created.Id}", created);
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<BatchRegisteredResponse>> AddMembers(Guid id, [FromBody] AddBatchMembersRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Ok(await sender.Send(new AddBatchMembersCommand(id, body.Members ?? [], body.ServiceIds, body.ConfirmFrequencyWarnings, body.UtcOffsetMinutes), cancellationToken));
    }
}
