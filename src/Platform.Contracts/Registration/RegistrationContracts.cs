namespace Platform.Contracts.Registration;

// Request and response shapes of the Registration module (intake LIS-INT-REG-001). Validation here is shape only; the
// rules live in the Visit, Booking and Batch aggregates and in the order planner. Enum values travel as their names.

// ---- ordering ------------------------------------------------------------------------------------------------------

/// <summary>A service to order; <paramref name="Reserved"/> keeps it billed without a tube (BR-05).</summary>
public sealed record OrderLineRequest(Guid ServiceId, bool Reserved);

public sealed record PreparationAnswerRequest(Guid PreparationId, string Answer);

/// <summary><paramref name="Kind"/> is <c>Amount</c> or <c>Percent</c>; the reason is a catalogue reason of kind Discount.</summary>
public sealed record VisitDiscountRequest(string Kind, decimal Value, Guid ReasonId);

/// <summary>
/// A sample of the order marked at registration (LIS-CR-008 C-02): one tube of the dry run (<paramref name="TubeKey"/> =
/// <c>TubePreview.Key</c>) or, without a key, every tube of <paramref name="SampleTypeId"/>. Only entries with
/// <paramref name="CollectedOutside"/> true count; <paramref name="CollectedAtUtc"/> is the collection time the clerk typed
/// (optional, default the registration time; REG-041 in the future or older than 30 days).
/// </summary>
public sealed record OrderSampleRequest(string? TubeKey, Guid? SampleTypeId, bool CollectedOutside, DateTimeOffset? CollectedAtUtc);

/// <summary>
/// CP-06 Save. <paramref name="UtcOffsetMinutes"/> is the desk's offset (east positive) for test days (BR-13).
/// <paramref name="Samples"/> marks the samples collected outside the laboratory one by one (LIS-CR-008 C-02); the
/// visit-level <paramref name="CollectedOutside"/> is the old contract, kept for one release: sent true without
/// <paramref name="Samples"/>, it marks every tube.
/// </summary>
public sealed record RegisterVisitRequest(
    Guid PatientId,
    string? TreatmentNo,
    Guid? ReferringDoctorId,
    Guid PayerId,
    Guid ContractTierId,
    string? InsuranceId,
    Guid? DiagnosisId,
    Guid? BookingId,
    bool Urgent,
    bool Biohazard,
    bool CollectedOutside,
    IReadOnlyList<OrderLineRequest> Lines,
    IReadOnlyList<PreparationAnswerRequest>? PreparationAnswers,
    VisitDiscountRequest? Discount,
    bool ConfirmFrequencyWarnings,
    int? UtcOffsetMinutes,
    IReadOnlyList<OrderSampleRequest>? Samples = null);

public sealed record VisitRegisteredResponse(Guid Id, string Accession, int TubeCount, IReadOnlyList<OrderWarning> Warnings);

/// <summary>The dry run of CP-06 (nothing saved): a saved visit (<paramref name="VisitId"/>) checks added services against its live lines.</summary>
public sealed record CheckOrderRequest(
    Guid? VisitId,
    Guid? PatientId,
    Guid PayerId,
    Guid ContractTierId,
    Guid? DiagnosisId,
    IReadOnlyList<OrderLineRequest> Lines,
    int? UtcOffsetMinutes);

/// <summary>A refusal the planner found (the same code Register would answer); <c>Detail</c> names what a REG-109 refusal repeats.</summary>
public sealed record OrderIssue(string Code, Guid? ServiceId, string? ServiceCode, string Message, string? Detail = null);

/// <summary>An advisory warning (REG-2xx): frequency alert, contract alert, precaution, added by rule or diagnosis, skipped companion.</summary>
public sealed record OrderWarning(
    string Code,
    Guid? ServiceId,
    string? ServiceCode,
    string? ServiceNameEn,
    string? ServiceNameAr,
    string? Detail,
    string? DetailAr,
    DateTimeOffset? LastOrderedAtUtc,
    string? LastAccession,
    int? WindowDays);

public sealed record OrderLinePreview(
    Guid ServiceId,
    string Code,
    string NameEn,
    string NameAr,
    string Type,
    string Kind,
    string Status,
    bool Priced,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    DateTimeOffset? ExpectedResultAtUtc,
    string? Precaution,
    string? AlertCondition,
    string Origin,
    Guid? AddedByRuleId,
    string? RuleNameEn,
    string? RuleNameAr,
    string? TubeKey,
    IReadOnlyList<OrderLinePreview> Members);

/// <summary>A tube the order produces or joins (BR-02); <paramref name="Key"/> is <c>group:type:condition</c>.</summary>
public sealed record TubePreview(
    string Key,
    Guid? ExistingTubeId,
    string? SampleNumber,
    string UnitCode,
    string ClinicalGroupNameEn,
    string ClinicalGroupNameAr,
    string SampleTypeNameEn,
    string SampleTypeNameAr,
    string? SampleConditionNameEn,
    string? SampleConditionNameAr,
    IReadOnlyList<Guid> ServiceIds);

public sealed record OrderTotals(decimal TotalListPrice, decimal PatientShareTotal, decimal PayerShareTotal);

public sealed record PreparationQuestionItem(Guid PreparationId, string Code, string QuestionEn, string QuestionAr, string AnswerType, IReadOnlyList<string> Options, string Gender, IReadOnlyList<Guid> ServiceIds);

public sealed record OrderCheckResult(
    IReadOnlyList<OrderLinePreview> Lines,
    IReadOnlyList<TubePreview> Tubes,
    OrderTotals Totals,
    IReadOnlyList<OrderIssue> Issues,
    IReadOnlyList<OrderWarning> Warnings,
    IReadOnlyList<PreparationQuestionItem> Preparations,
    string Currency);

/// <summary>What a typed entry resolved to (CP-06 service entry): <c>Service</c>, <c>ShortcutSet</c> or <c>None</c> (the picker opens with the candidates).</summary>
public sealed record ServiceEntryResult(string Kind, IReadOnlyList<ServiceEntryItem> Services, string? SetCode, string? SetNameEn, string? SetNameAr, IReadOnlyList<ServiceEntryItem> Candidates);

public sealed record ServiceEntryItem(Guid Id, string Code, string ShortName, int? ShortcutNo, string NameEn, string NameAr, string Type);

// ---- saved visit ---------------------------------------------------------------------------------------------------

public sealed record AddVisitLinesRequest(IReadOnlyList<OrderLineRequest> Lines, IReadOnlyList<PreparationAnswerRequest>? PreparationAnswers, bool ConfirmFrequencyWarnings);

public sealed record VisitLinesAddedResponse(int AddedLines, int NewTubes, IReadOnlyList<OrderWarning> Warnings);

public sealed record ReleaseLinesRequest(IReadOnlyList<Guid> LineIds);

/// <summary>V4: a reason id of kind Cancellation or a free-text reason (one of them).</summary>
public sealed record CancelLinesRequest(IReadOnlyList<Guid> LineIds, Guid? ReasonId, string? Reason);

/// <summary>V5. <paramref name="CollectedOutside"/> is accepted and ignored since LIS-CR-008 C-02: the flag belongs to each tube and is set at registration.</summary>
public sealed record UpdateVisitDetailsRequest(string? TreatmentNo, Guid? ReferringDoctorId, string? InsuranceId, bool Urgent, bool Biohazard, bool? CollectedOutside = null);

/// <summary>V6: <paramref name="Discount"/> null clears it (<paramref name="Reason"/> optional then).</summary>
public sealed record SetVisitDiscountRequest(VisitDiscountRequest? Discount, string? Reason);

/// <summary>V7: a reason id of kind Transfer or a free-text reason (one of them).</summary>
public sealed record TransferContractRequest(Guid PayerId, Guid ContractTierId, Guid? ReasonId, string? Reason);

/// <summary>V8: the new gross patient share and payer share of the line (their sum must stay the same).</summary>
public sealed record AdjustLineSharesRequest(decimal PatientShare, decimal PayerShare, Guid? ReasonId, string? Reason);

public sealed record ContractRef(Guid PayerId, string PayerNameEn, string PayerNameAr, Guid ContractTierId, string TierNameEn, string TierNameAr);

public sealed record TransferPreviewLine(
    Guid LineId,
    Guid ServiceId,
    string Code,
    string NameEn,
    string NameAr,
    bool Priced,
    decimal ListPriceBefore,
    decimal PatientShareBefore,
    decimal PayerShareBefore,
    decimal DiscountBefore,
    decimal ListPriceAfter,
    decimal PatientShareAfter,
    decimal PayerShareAfter,
    decimal DiscountAfter);

public sealed record TransferPreview(
    Guid VisitId,
    string Accession,
    ContractRef From,
    ContractRef To,
    IReadOnlyList<TransferPreviewLine> Lines,
    bool AllPriced,
    decimal PatientShareBefore,
    decimal PatientShareAfter,
    decimal PayerShareBefore,
    decimal PayerShareAfter,
    decimal Paid,
    decimal Refunded,
    decimal RemainingBefore,
    decimal RemainingAfter);

// ---- registers -----------------------------------------------------------------------------------------------------

public sealed record VisitLineChip(Guid LineId, string Code, string NameEn, string NameAr, string Status, string Kind);

/// <summary>A row of CP-10. Patient names and mobile are null for a VIP patient without the See-VIP right (<paramref name="PatientMasked"/>).</summary>
public sealed record VisitListItem(
    Guid Id,
    string Accession,
    DateTimeOffset RegisteredAtUtc,
    Guid BranchId,
    string? BranchNameEn,
    string? BranchNameAr,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    string? Mobile,
    bool PatientMasked,
    Guid PayerId,
    string? PayerNameEn,
    string? PayerNameAr,
    string? TierNameEn,
    string? TierNameAr,
    string Status,
    string LocationType,
    bool Urgent,
    string? BatchNumber,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    decimal? Paid,
    decimal? Remaining,
    IReadOnlyList<VisitLineChip> Lines);

public sealed record VisitLineItem(
    Guid Id,
    Guid ServiceId,
    string Code,
    string NameEn,
    string NameAr,
    string ServiceType,
    string Kind,
    Guid? ParentLineId,
    string Status,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    decimal Discount,
    DateTimeOffset? ExpectedResultAtUtc,
    Guid? TubeId,
    string? SampleNumber,
    Guid? CollectorUserId,
    string? CollectorName,
    string Origin,
    DateTimeOffset OrderedAtUtc,
    string? OrderedByName,
    DateTimeOffset? ReleasedAtUtc,
    string? CancellationReason,
    string? CancelledByName,
    DateTimeOffset? CancelledAtUtc);

public sealed record VisitTubeItem(
    Guid Id,
    string SampleNumber,
    string UnitCode,
    Guid ClinicalGroupId,
    string ClinicalGroupNameEn,
    string ClinicalGroupNameAr,
    Guid SampleTypeId,
    string SampleTypeNameEn,
    string SampleTypeNameAr,
    string? SampleConditionNameEn,
    string? SampleConditionNameAr,
    bool IsLive,
    IReadOnlyList<string> ServiceCodes,
    bool CollectedOutside = false,
    DateTimeOffset? CollectedOutsideAtUtc = null);

public sealed record PreparationAnswerItem(Guid PreparationId, string QuestionEn, string QuestionAr, string Answer, DateTimeOffset AnsweredAtUtc);

public sealed record VisitTransferItem(
    Guid Id,
    string Kind,
    Guid? LineId,
    string? ServiceCode,
    string? FromPayerNameEn,
    string? FromPayerNameAr,
    string? FromTierNameEn,
    string? FromTierNameAr,
    string? ToPayerNameEn,
    string? ToPayerNameAr,
    string? ToTierNameEn,
    string? ToTierNameAr,
    decimal PatientShareBefore,
    decimal PatientShareAfter,
    decimal PayerShareBefore,
    decimal PayerShareAfter,
    string Reason,
    string? ActorName,
    DateTimeOffset AtUtc);

public sealed record VisitBalanceView(decimal PatientShareTotal, decimal Paid, decimal Refunded, decimal Remaining, decimal Refundable);

public sealed record VisitPatientView(
    Guid Id,
    string? Number,
    string? Title,
    string? FullName,
    string? NameAr,
    string? Gender,
    int AgeYears,
    int AgeMonths,
    int AgeDays,
    string? Mobile,
    string? NationalId,
    bool IsVip,
    string? Status,
    bool Masked);

public sealed record VisitDetail(
    Guid Id,
    string Accession,
    string Status,
    string LocationType,
    Guid BranchId,
    string? BranchNameEn,
    string? BranchNameAr,
    VisitPatientView Patient,
    Guid? OriginalPatientId,
    ContractRef Contract,
    Guid? ReferringDoctorId,
    string? DoctorNameEn,
    string? DoctorNameAr,
    string? TreatmentNo,
    string? InsuranceId,
    Guid? DiagnosisId,
    Guid? BookingId,
    Guid? BatchId,
    string? BatchNumber,
    bool Urgent,
    bool Biohazard,
    bool CollectedOutside,
    DateTimeOffset RegisteredAtUtc,
    string? RegisteredByName,
    DateTimeOffset? CompletedAtUtc,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    string? DiscountKind,
    decimal DiscountValue,
    Guid? DiscountReasonId,
    string? DiscountReason,
    int UtcOffsetMinutes,
    IReadOnlyList<VisitLineItem> Lines,
    IReadOnlyList<VisitTubeItem> Tubes,
    IReadOnlyList<PreparationAnswerItem> PreparationAnswers,
    IReadOnlyList<VisitTransferItem> Transfers,
    VisitBalanceView? Balance,
    uint RowVersion);

public sealed record CancelledLineItem(
    Guid LineId,
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    Guid BranchId,
    string Code,
    string NameEn,
    string NameAr,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    string? Reason,
    string? CancelledByName,
    DateTimeOffset? CancelledAtUtc);

public sealed record TransferLogItem(
    Guid Id,
    Guid VisitId,
    string Accession,
    string Kind,
    string? ServiceCode,
    string? FromPayerNameEn,
    string? FromPayerNameAr,
    string? FromTierNameEn,
    string? FromTierNameAr,
    string? ToPayerNameEn,
    string? ToPayerNameAr,
    string? ToTierNameEn,
    string? ToTierNameAr,
    decimal PatientShareBefore,
    decimal PatientShareAfter,
    decimal PayerShareBefore,
    decimal PayerShareAfter,
    string Reason,
    string? ActorName,
    DateTimeOffset AtUtc);

/// <summary>What the registration page needs from the tenant parameters and the actor's special rights (read-only).</summary>
public sealed record RegistrationPageSettings(
    bool AutoPrintLabels,
    bool AutoPrintJobOrder,
    bool AutoPrintReceipt,
    int LabelCopies,
    bool MandatoryMobile,
    bool MandatoryDoctor,
    bool MandatoryNationalId,
    int DiscountCapPercent,
    bool CanCancel,
    bool CanTransfer,
    bool CanPay,
    bool CanSeeVip,
    string Currency,
    string? BranchNameEn,
    string? BranchNameAr);

// ---- bookings (CP-07) ----------------------------------------------------------------------------------------------

public sealed record BookingRequest(
    Guid? PatientId,
    string? PatientName,
    string? Gender,
    string? Mobile,
    int? AgeYears,
    string Address,
    DateTimeOffset VisitAtUtc,
    Guid? PhlebotomistUserId,
    Guid PayerId,
    Guid ContractTierId,
    IReadOnlyList<Guid> ServiceIds,
    string? Notes);

public sealed record CancelBookingRequest(string Reason);

public sealed record BookingCreatedResponse(Guid Id, string Number);

public sealed record BookingListItem(
    Guid Id,
    string Number,
    string Status,
    DateTimeOffset VisitAtUtc,
    Guid BranchId,
    Guid? PatientId,
    string? PatientNumber,
    string? PatientName,
    string? Mobile,
    string Address,
    Guid? PhlebotomistUserId,
    string? PhlebotomistName,
    Guid PayerId,
    string? PayerNameEn,
    string? PayerNameAr,
    string? TierNameEn,
    string? TierNameAr,
    int ServiceCount,
    Guid? VisitId,
    string? Accession);

public sealed record BookingServiceItem(Guid ServiceId, string Code, string NameEn, string NameAr);

public sealed record BookingDetail(
    BookingListItem Summary,
    string? Gender,
    int? AgeYears,
    Guid ContractTierId,
    string? Notes,
    IReadOnlyList<BookingServiceItem> Services,
    DateTimeOffset? ConvertedAtUtc,
    string? ConvertedByName,
    string? CancellationReason,
    string? CancelledByName,
    DateTimeOffset? CancelledAtUtc,
    uint RowVersion);

// ---- batches (CP-08, CP-09) ----------------------------------------------------------------------------------------

/// <summary>A member row: the patient file, the services (lab-to-lab: per row; group: the batch's) and, for lab-to-lab, whether the sample was drawn outside.</summary>
public sealed record BatchMemberRequest(Guid PatientId, IReadOnlyList<Guid>? ServiceIds, bool CollectedOutside);

/// <summary><paramref name="Kind"/> is <c>Group</c> (payer, tier, group name, services once) or <c>LabToLab</c> (sending lab, services per row).</summary>
public sealed record RegisterBatchRequest(
    string Kind,
    Guid? PayerId,
    Guid? ContractTierId,
    Guid? SendingLabId,
    Guid? ReferringDoctorId,
    string? GroupName,
    string? Notes,
    IReadOnlyList<Guid>? ServiceIds,
    IReadOnlyList<BatchMemberRequest> Members,
    bool ConfirmFrequencyWarnings,
    int? UtcOffsetMinutes);

public sealed record AddBatchMembersRequest(IReadOnlyList<BatchMemberRequest> Members, IReadOnlyList<Guid>? ServiceIds, bool ConfirmFrequencyWarnings, int? UtcOffsetMinutes);

public sealed record BatchMemberVisit(Guid PatientId, Guid VisitId, string Accession);

public sealed record BatchRegisteredResponse(Guid Id, string Number, IReadOnlyList<BatchMemberVisit> Visits);

public sealed record BatchListItem(
    Guid Id,
    string Number,
    string Kind,
    DateTimeOffset RegisteredAtUtc,
    Guid BranchId,
    string? GroupName,
    Guid? SendingLabId,
    string? SendingLabNameEn,
    string? SendingLabNameAr,
    Guid PayerId,
    string? PayerNameEn,
    string? PayerNameAr,
    string? TierNameEn,
    string? TierNameAr,
    int MemberCount,
    decimal PatientShareTotal,
    decimal PayerShareTotal);

public sealed record BatchMemberItem(
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string Status,
    bool CollectedOutside,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    decimal? Remaining,
    IReadOnlyList<VisitLineChip> Lines);

public sealed record BatchDetail(
    BatchListItem Summary,
    Guid? ReferringDoctorId,
    string? DoctorNameEn,
    string? DoctorNameAr,
    string? CourierEn,
    string? CourierAr,
    string? Notes,
    string? RegisteredByName,
    IReadOnlyList<BatchMemberItem> Members);

/// <summary>A sending laboratory as the lab-to-lab page uses it (CP-09: fills payer, tier, courier, phone, doctor).</summary>
public sealed record SendingLabOption(
    Guid Id,
    string NameEn,
    string NameAr,
    Guid PayerId,
    Guid ContractTierId,
    string? CourierEn,
    string? CourierAr,
    string? Phone,
    Guid? DoctorId,
    bool IsActive);

// ---- documents -----------------------------------------------------------------------------------------------------

public sealed record RegistrationLetterhead(string LabNameEn, string LabNameAr, string? BranchNameEn, string? BranchNameAr);

/// <summary>
/// One label per live tube (LDM §2.2 label content); printed <see cref="VisitLabelsDocument.Copies"/> times each. The
/// label marks an urgent visit ("!") and a biohazard visit ("BIO", validation 2026-10-11).
/// </summary>
public sealed record SampleLabel(
    Guid TubeId,
    string SampleNumber,
    string UnitCode,
    string SampleTypeNameEn,
    string SampleTypeNameAr,
    string? SampleConditionNameEn,
    string? SampleConditionNameAr,
    string Accession,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    string? Gender,
    int AgeYears,
    DateTimeOffset RegisteredAtUtc,
    string? BranchNameEn,
    string? BranchNameAr,
    bool Urgent,
    IReadOnlyList<string> ServiceCodes,
    bool Biohazard,
    bool CollectedOutside = false);

public sealed record VisitLabelsDocument(RegistrationLetterhead Letterhead, string Accession, int Copies, int UtcOffsetMinutes, IReadOnlyList<SampleLabel> Labels);

public sealed record JobOrderLine(
    Guid LineId,
    string Code,
    string NameEn,
    string NameAr,
    string Kind,
    string Status,
    string? UnitCode,
    string? SampleNumber,
    string? SampleTypeNameEn,
    string? SampleTypeNameAr,
    DateTimeOffset? ExpectedResultAtUtc);

public sealed record JobOrderDocument(
    RegistrationLetterhead Letterhead,
    Guid VisitId,
    string Accession,
    DateTimeOffset RegisteredAtUtc,
    string? RegisteredByName,
    VisitPatientView Patient,
    int VisitOrdinal,
    ContractRef Contract,
    string? DoctorNameEn,
    string? DoctorNameAr,
    bool Urgent,
    bool Biohazard,
    bool CollectedOutside,
    string? BatchNumber,
    IReadOnlyList<JobOrderLine> Lines,
    IReadOnlyList<VisitTubeItem> Tubes,
    IReadOnlyList<PreparationAnswerItem> PreparationAnswers,
    VisitBalanceView? Balance,
    string Currency,
    int UtcOffsetMinutes,
    string? PrintedBy,
    DateTimeOffset PrintedAtUtc);

/// <summary>CP-12: payer/tier (null = the cash default) and the services; the patient's gender filters the preparations.</summary>
public sealed record PriceQuoteRequest(Guid? PayerId, Guid? ContractTierId, IReadOnlyList<Guid> ServiceIds, string? Gender, int? UtcOffsetMinutes);

public sealed record PriceQuoteDocumentLine(
    Guid ServiceId,
    string Code,
    string NameEn,
    string NameAr,
    string Type,
    bool Priced,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    DateTimeOffset? ExpectedResultAtUtc,
    string? Precaution,
    string? AlertCondition);

public sealed record PriceQuoteDocument(
    RegistrationLetterhead Letterhead,
    ContractRef? Contract,
    string Currency,
    IReadOnlyList<PriceQuoteDocumentLine> Lines,
    decimal TotalListPrice,
    decimal TotalPatientShare,
    decimal TotalPayerShare,
    IReadOnlyList<PreparationQuestionItem> Preparations,
    IReadOnlyList<OrderIssue> Issues,
    int UtcOffsetMinutes,
    string? PrintedBy,
    DateTimeOffset PrintedAtUtc);
