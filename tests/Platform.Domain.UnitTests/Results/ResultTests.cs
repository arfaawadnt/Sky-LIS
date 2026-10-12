using Platform.Domain.Catalogue;
using Platform.Domain.Results;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Results;

/// <summary>Acceptance scenarios of intake LIS-INT-RES-001 §16: one test per transition row, per refused precondition and per invariant (03 §10).</summary>
public sealed class ResultTests
{
    private static readonly Guid Technician = Guid.CreateVersion7();
    private static readonly Guid Chemist = Guid.CreateVersion7();
    private static readonly Guid Registrar = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static readonly ResultActor Bench = new(Technician, "Bench Technician", ResultRightsGrant.All);
    private static readonly ResultActor Doctor = new(Chemist, "Dr. Head", ResultRightsGrant.All);

    private static readonly ResolvedRange Potassium = ResolvedRange.Create(3.5m, 5.0m, 2.5m, 6.5m, 0.1m, 15m, null)!;
    private static readonly ResultTestRules Numeric1 = ResultTestRules.For(LabTestType.Numeric, decimals: 1, unit: "mmol/L");
    private static readonly ResultTestRules Delta20 = ResultTestRules.For(LabTestType.Numeric, decimals: 0, unit: "U/L", deltaPercent: 20m);

    private static readonly IReadOnlyList<ResultListOption> UrineColour =
    [
        new(".", true, false, true),
        new("Yellow", true, false, false),
        new("Red", false, false, false),
        new("Black", false, true, false),
    ];

    private static Result Ordered(LabTestType type = LabTestType.Numeric) =>
        Result.Order(new ResultOrder(
            Guid.CreateVersion7(), "ACC-2026-00001", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), type, false, Guid.CreateVersion7(),
            Guid.CreateVersion7(), "unit-key", Guid.CreateVersion7(), Guid.CreateVersion7(), null, false, 10, 1, Now.AddHours(-2), Now.AddHours(22), Registrar));

    private static Result Complete(decimal value = 4.2m)
    {
        var result = Ordered();
        result.Enter(ResultValueInput.Number(value), Numeric1, Potassium, null, Bench, true, Now);
        result.ClearDomainEvents();
        return result;
    }

    private static Result Verified()
    {
        var result = Complete();
        result.Verify(Doctor, false, Now.AddMinutes(5));
        result.ClearDomainEvents();
        return result;
    }

    private static ResultActor With(Func<ResultRightsGrant, ResultRightsGrant> change) => new(Chemist, "Dr. Head", change(ResultRightsGrant.All));

    private static void Refused(string code, Action action) => Assert.Equal(code, Assert.ThrowsAny<DomainException>(action).Code);

    [Fact]
    public void R_T1_ok_Order_records_an_Ordered_result_with_one_log_row()
    {
        var result = Ordered();

        Assert.Equal(ResultStatus.Ordered, result.Status);
        Assert.False(result.HasValue);
        Assert.Equal(Now.AddHours(-2), result.ClockStartUtc);
        Assert.Empty(result.DomainEvents);
        var row = Assert.Single(result.Log);
        Assert.Equal((ResultLogKind.Ordered, ResultStatus.Ordered, Registrar), (row.Kind, row.ToStatus, row.ByUserId));
    }

    [Fact]
    public void R_T1_refused_without_its_references()
    {
        Refused(ResultsCodes.ReferenceRequired, () => Result.Order(new ResultOrder(
            Guid.Empty, "ACC", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), LabTestType.Numeric, false, null,
            Guid.CreateVersion7(), "key", Guid.CreateVersion7(), Guid.CreateVersion7(), null, false, 0, 0, Now, Now, null)));
    }

    [Fact]
    public void R_T2_ok_a_numeric_value_is_rounded_flagged_against_the_range_and_logged()
    {
        var result = Ordered();

        var changed = result.Enter(ResultValueInput.Number(6.84m), Numeric1, Potassium, null, Bench, true, Now);

        Assert.True(changed);
        Assert.Equal(ResultStatus.Complete, result.Status);
        Assert.Equal((6.8m, "6.8", ResultFlag.CriticalHigh, true), (result.NumericValue, result.TextValue, result.Flag, result.IsPanic));
        Assert.Equal(("mmol/L", 1), (result.Unit, result.Decimals));
        Assert.Equal((Technician, Now), (result.EnteredByUserId, result.EnteredAtUtc));
        Assert.Contains(result.Log, row => row.Kind == ResultLogKind.Value && row.ValueAfter == "6.8" && row.Source == ResultSource.Manual);
        Assert.Contains(result.Log, row => row.Kind == ResultLogKind.Status && row.FromStatus == ResultStatus.Ordered && row.ToStatus == ResultStatus.Complete);
        Assert.Contains(result.DomainEvents, e => e is ResultEntered entered && entered.IsPanic && entered.Flag == "CriticalHigh");
        Assert.Contains(result.DomainEvents, e => e is ResultPanicDetected);
    }

    [Theory]
    [InlineData(4.2, ResultFlag.Normal)]
    [InlineData(3.4, ResultFlag.Low)]
    [InlineData(5.1, ResultFlag.High)]
    [InlineData(2.4, ResultFlag.CriticalLow)]
    [InlineData(6.6, ResultFlag.CriticalHigh)]
    public void R_T2_flags_follow_the_resolved_range(double value, ResultFlag flag)
    {
        var result = Ordered();
        result.Enter(ResultValueInput.Number((decimal)value), Numeric1, Potassium, null, Bench, true, Now);
        Assert.Equal(flag, result.Flag);
    }

    [Fact]
    public void R_T2_reportable_limits_print_as_less_or_greater_than()
    {
        var low = Ordered();
        low.Enter(ResultValueInput.Number(0.04m), Numeric1, Potassium, null, Bench, true, Now);
        var high = Ordered();
        high.Enter(ResultValueInput.Of("16.3"), Numeric1, Potassium, null, Bench, true, Now);

        Assert.Equal(("< 0.1", 0.0m), (low.TextValue, low.NumericValue));
        Assert.Equal(("> 15.0", 16.3m), (high.TextValue, high.NumericValue));
    }

    [Fact]
    public void R_T2_unchanged_value_is_not_logged_again()
    {
        var result = Complete(4.2m);
        var rows = result.Log.Count;

        Assert.False(result.Enter(ResultValueInput.Number(4.2m), Numeric1, Potassium, null, Bench, true, Now.AddMinutes(1)));
        Assert.Equal(rows, result.Log.Count);
    }

    [Fact]
    public void R_T2_gate_refused_while_the_sample_is_not_received()
    {
        var result = Ordered();
        Refused(ResultsCodes.SampleNotReceived, () => result.Enter(ResultValueInput.Number(4m), Numeric1, Potassium, null, Bench, false, Now));
        Assert.Equal(ResultStatus.Ordered, result.Status);
        Assert.False(result.HasValue);
    }

    [Fact]
    public void R_T2_right_refused_without_the_edit_right()
    {
        var result = Ordered();
        Refused(ResultsCodes.NoEditRight, () => result.Enter(ResultValueInput.Number(4m), Numeric1, Potassium, null, With(rights => rights with { CanEdit = false }), true, Now));
    }

    [Theory]
    [InlineData(null, ResultsCodes.ValueRequired)]
    [InlineData("abc", ResultsCodes.NumberInvalid)]
    public void R_T2_number_refused(string? text, string code)
    {
        var result = Ordered();
        Refused(code, () => result.Enter(new ResultValueInput(null, text, null, null), Numeric1, Potassium, null, Bench, true, Now));
    }

    [Fact]
    public void R_T2_list_restricted_accepts_listed_values_only_and_never_the_placeholder()
    {
        var restricted = ResultTestRules.For(LabTestType.RestrictedList, listValues: UrineColour);
        var result = Ordered(LabTestType.RestrictedList);

        Refused(ResultsCodes.ValueNotInList, () => result.Enter(ResultValueInput.Of("Green"), restricted, null, null, Bench, true, Now));
        Refused(ResultsCodes.PlaceholderNotAResult, () => result.Enter(ResultValueInput.Of("."), restricted, null, null, Bench, true, Now));

        result.Enter(ResultValueInput.Of("black"), restricted, null, null, Bench, true, Now);
        Assert.Equal(("Black", ResultFlag.Abnormal, true), (result.TextValue, result.Flag, result.IsPanic));
    }

    [Fact]
    public void R_T2_list_optional_keeps_free_text_unflagged()
    {
        var optional = ResultTestRules.For(LabTestType.OptionalList, listValues: UrineColour);
        var free = Ordered(LabTestType.OptionalList);
        free.Enter(ResultValueInput.Of("Amber"), optional, null, null, Bench, true, Now);
        var listed = Ordered(LabTestType.OptionalList);
        listed.Enter(ResultValueInput.Of("Red"), optional, null, null, Bench, true, Now);

        Assert.Equal(("Amber", (ResultFlag?)null), (free.TextValue, free.Flag));
        Assert.Equal(ResultFlag.Abnormal, listed.Flag);
        Assert.False(listed.IsPanic);
    }

    [Fact]
    public void R_T2_formula_is_never_typed_and_computes_from_its_inputs()
    {
        var formula = ResultTestRules.For(LabTestType.Formula, decimals: 2);
        var result = Ordered(LabTestType.Formula);

        Refused(ResultsCodes.FormulaNotTyped, () => result.Enter(ResultValueInput.Number(1m), formula, null, null, Bench, true, Now));
        Assert.False(result.ApplyComputed(null, formula, null, null, ResultSource.Manual, Technician, "Bench", Now));
        Assert.True(result.ApplyComputed(1.23456m, formula, null, null, ResultSource.Manual, Technician, "Bench", Now));
        Assert.Equal((ResultStatus.Complete, 1.23m, "1.23"), (result.Status, result.NumericValue, result.TextValue));
        Assert.False(result.ApplyComputed(1.231m, formula, null, null, ResultSource.Manual, Technician, "Bench", Now));
    }

    [Fact]
    public void R_T2b_a_verified_formula_reopens_when_its_value_changes()
    {
        var formula = ResultTestRules.For(LabTestType.Formula, decimals: 1);
        var result = Ordered(LabTestType.Formula);
        result.ApplyComputed(10m, formula, null, null, ResultSource.Manual, Technician, "Bench", Now);
        result.Verify(Doctor, false, Now);

        Assert.True(result.ApplyComputed(12m, formula, null, null, ResultSource.Manual, Technician, "Bench", Now.AddMinutes(1)));
        Assert.Equal((ResultStatus.Complete, (Guid?)null, (string?)null), (result.Status, result.VerifiedByUserId, result.VerifiedByName));
    }

    [Fact]
    public void R_T2_text_culture_and_formatted_reports()
    {
        var text = Ordered(LabTestType.NonNumeric);
        text.Enter(ResultValueInput.Of(" Negative "), ResultTestRules.For(LabTestType.NonNumeric), null, null, Bench, true, Now);
        Assert.Equal("Negative", text.TextValue);

        var culture = Ordered(LabTestType.Culture);
        var rules = ResultTestRules.For(LabTestType.Culture);
        Refused(ResultsCodes.CultureInvalid, () => culture.Enter(new ResultValueInput(null, null, null, null), rules, null, null, Bench, true, Now));
        Refused(ResultsCodes.CultureInvalid, () => CultureReport.Create(false, []));
        var ecoli = new CultureIsolate(Guid.CreateVersion7(), "ECOLI", "E. coli", "إشريكية قولونية", "10^5 CFU/mL", null,
            [new CultureSensitivity(Guid.CreateVersion7(), "AMC", "AMC", "Amoxicillin-clavulanate", "أموكسيسيلين", "s", "4")]);
        culture.Enter(new ResultValueInput(null, null, null, CultureReport.Create(false, [ecoli])), rules, null, null, Bench, true, Now);
        Assert.Equal(("Growth: E. coli (10^5 CFU/mL)", ResultFlag.Abnormal), (culture.TextValue, culture.Flag));
        Assert.Equal("S", culture.Culture!.Isolates[0].Sensitivities[0].Interpretation);

        var none = Ordered(LabTestType.Culture);
        none.Enter(new ResultValueInput(null, null, null, CultureReport.Create(true, [])), rules, null, null, Bench, true, Now);
        Assert.Equal(("No growth", ResultFlag.Normal), (none.TextValue, none.Flag));

        var histology = Ordered(LabTestType.FormattedText);
        Refused(ResultsCodes.ParagraphsInvalid, () => FormattedReport.Create([new FormattedParagraph("Gross", " ")]));
        histology.Enter(new ResultValueInput(null, null, FormattedReport.Create([new FormattedParagraph("Gross", "Two fragments."), new FormattedParagraph("", "")]), null), ResultTestRules.For(LabTestType.FormattedText), null, null, Bench, true, Now);
        Assert.True(histology.HasValue);
        Assert.Single(histology.Formatted!.Paragraphs);
        Assert.Null(histology.TextValue);
    }

    [Fact]
    public void R_T2_delta_flag_must_be_acknowledged_before_verification()
    {
        var result = Ordered();
        var previous = new PreviousResultValue(Guid.CreateVersion7(), 100m, Now.AddDays(-30));
        result.Enter(ResultValueInput.Number(130m), Delta20, null, previous, Bench, true, Now);

        Assert.True(result.DeltaFlag);
        Assert.Equal((30.0m, 100m, previous.ResultId), (result.DeltaPercent, result.PreviousValue, result.PreviousResultId));
        Refused(ResultsCodes.DeltaNotAcknowledged, () => result.Verify(Doctor, false, Now));

        result.AcknowledgeDelta(Doctor, "Checked with the patient's physician.", Now);
        Assert.Equal((Chemist, Now), (result.DeltaAcknowledgedByUserId, result.DeltaAcknowledgedAtUtc));
        Assert.Contains(result.DomainEvents, e => e is ResultDeltaAcknowledged);
        Refused(ResultsCodes.NoDeltaToAcknowledge, () => result.AcknowledgeDelta(Doctor, null, Now));

        result.Verify(Doctor, false, Now);
        Assert.Equal(ResultStatus.Verified, result.Status);
    }

    [Fact]
    public void R_BR11_recheck_a_previous_verified_after_entry_flags_the_result_before_verification()
    {
        // Validation A-02 (2026-10-11): CREA 2.0 entered while the patient's CREA 1.2 was not verified yet.
        var result = Ordered();
        result.Enter(ResultValueInput.Number(2.0m), Delta20, null, null, Bench, true, Now);
        Assert.False(result.DeltaFlag);

        var previous = new PreviousResultValue(Guid.CreateVersion7(), 1.2m, Now.AddMinutes(10));
        Assert.True(result.RecheckDelta(previous, 20m));

        Assert.True(result.DeltaFlag);
        Assert.Equal((66.7m, 1.2m, previous.ResultId, previous.VerifiedAtUtc), (result.DeltaPercent!.Value, result.PreviousValue!.Value, result.PreviousResultId!.Value, result.PreviousVerifiedAtUtc!.Value));
        Refused(ResultsCodes.DeltaNotAcknowledged, () => result.Verify(Doctor, false, Now.AddMinutes(11)));
        Assert.False(result.RecheckDelta(previous, 20m));   // same basis: nothing to do
    }

    [Fact]
    public void R_BR11_recheck_a_newer_basis_clears_the_old_acknowledgement_and_an_older_one_is_ignored()
    {
        var result = Ordered();
        var first = new PreviousResultValue(Guid.CreateVersion7(), 100m, Now.AddDays(-2));
        result.Enter(ResultValueInput.Number(130m), Delta20, null, first, Bench, true, Now);
        result.AcknowledgeDelta(Doctor, "Known trend.", Now);

        Assert.False(result.RecheckDelta(new PreviousResultValue(Guid.CreateVersion7(), 50m, Now.AddDays(-5)), 20m));
        Assert.NotNull(result.DeltaAcknowledgedAtUtc);

        var newer = new PreviousResultValue(Guid.CreateVersion7(), 60m, Now.AddMinutes(1));
        Assert.True(result.RecheckDelta(newer, 20m));
        Assert.True(result.DeltaFlag);
        Assert.Null(result.DeltaAcknowledgedAtUtc);
        Refused(ResultsCodes.DeltaNotAcknowledged, () => result.Verify(Doctor, false, Now.AddMinutes(2)));

        Assert.True(result.RecheckDelta(new PreviousResultValue(Guid.CreateVersion7(), 125m, Now.AddMinutes(3)), 20m));
        Assert.False(result.DeltaFlag);
        result.Verify(Doctor, false, Now.AddMinutes(4));
        Assert.Equal(ResultStatus.Verified, result.Status);
    }

    [Fact]
    public void R_BR11_recheck_leaves_signed_results_and_tests_without_a_delta_limit_alone()
    {
        var verified = Verified();
        var previous = new PreviousResultValue(Guid.CreateVersion7(), 1m, Now.AddHours(1));
        Assert.False(verified.RecheckDelta(previous, 20m));
        Assert.False(verified.DeltaFlag);

        var open = Complete();
        Assert.False(open.RecheckDelta(previous, null));
        Assert.False(open.DeltaFlag);
    }

    [Fact]
    public void R_T2_delta_within_the_threshold_is_not_flagged()
    {
        var result = Ordered();
        result.Enter(ResultValueInput.Number(110m), Delta20, null, new PreviousResultValue(Guid.CreateVersion7(), 100m, Now.AddDays(-3)), Bench, true, Now);
        Assert.False(result.DeltaFlag);
        Assert.Equal(10.0m, result.DeltaPercent);
    }

    [Fact]
    public void R_T2_entering_a_reviewed_result_returns_it_to_Complete_and_a_verified_one_is_refused()
    {
        var reviewed = Complete();
        reviewed.Review(Doctor, Now);
        reviewed.Enter(ResultValueInput.Number(4.4m), Numeric1, Potassium, null, Bench, true, Now);
        Assert.Equal((ResultStatus.Complete, (Guid?)null), (reviewed.Status, reviewed.ReviewedByUserId));

        var verified = Verified();
        Refused(ResultsCodes.NotEditable, () => verified.Enter(ResultValueInput.Number(4.4m), Numeric1, Potassium, null, Bench, true, Now));
    }

    [Fact]
    public void R_T3_ok_and_refused_states_and_rights()
    {
        var result = Complete();
        result.Review(Doctor, Now);
        Assert.Equal((ResultStatus.Reviewed, Chemist, Now), (result.Status, result.ReviewedByUserId!.Value, result.ReviewedAtUtc!.Value));
        Assert.Contains(result.DomainEvents, e => e is ResultReviewed);

        Refused(ResultsCodes.NotReviewable, () => Ordered().Review(Doctor, Now));
        Refused(ResultsCodes.NoReviewRight, () => Complete().Review(With(rights => rights with { CanReview = false }), Now));
    }

    [Fact]
    public void R_T4_ok_Verify_records_the_signature()
    {
        var result = Complete();
        result.Verify(Doctor, false, Now);

        Assert.Equal((ResultStatus.Verified, Chemist, "Dr. Head", Now), (result.Status, result.VerifiedByUserId!.Value, result.VerifiedByName, result.VerifiedAtUtc!.Value));
        Assert.Contains(result.DomainEvents, e => e is ResultVerified verified && verified.ActorUserId == Chemist);
    }

    [Fact]
    public void R_T4_refused_without_value_without_right_or_review_first()
    {
        Refused(ResultsCodes.NoValue, () => Ordered().Verify(Doctor, false, Now));
        Refused(ResultsCodes.NoVerifyRight, () => Complete().Verify(With(rights => rights with { CanVerify = false }), false, Now));
        Refused(ResultsCodes.ReviewRequired, () => Complete().Verify(Doctor, true, Now));
        Refused(ResultsCodes.NotVerifiable, () => Verified().Verify(Doctor, false, Now));

        var reviewed = Complete();
        reviewed.Review(Doctor, Now);
        reviewed.Verify(Doctor, true, Now);
        Assert.Equal(ResultStatus.Verified, reviewed.Status);
    }

    [Fact]
    public void R_T5_undo_returns_to_the_previous_step_with_a_reason()
    {
        var result = Complete();
        result.Review(Doctor, Now);
        result.Verify(Doctor, false, Now);

        Refused(ResultsCodes.ReasonRequired, () => result.Undo(Doctor, " ", Now));
        result.Undo(Doctor, "Wrong patient label suspected.", Now);
        Assert.Equal((ResultStatus.Reviewed, (Guid?)null), (result.Status, result.VerifiedByUserId));
        Assert.Contains(result.DomainEvents, e => e is ResultUndone undone && undone.FromStatus == "Verified" && undone.ToStatus == "Reviewed" && undone.Reason == "Wrong patient label suspected.");

        result.Undo(Doctor, "Recheck.", Now);
        Assert.Equal((ResultStatus.Complete, (Guid?)null), (result.Status, result.ReviewedByUserId));
        Refused(ResultsCodes.NothingToUndo, () => result.Undo(Doctor, "again", Now));

        var unreviewed = Verified();
        unreviewed.Undo(Doctor, "Recheck.", Now);
        Assert.Equal(ResultStatus.Complete, unreviewed.Status);
    }

    [Fact]
    public void R_T5_printed_is_amended_not_undone()
    {
        var result = Verified();
        result.MarkPrinted(Doctor, false, null, Now);
        Refused(ResultsCodes.PrintedNotUndoable, () => result.Undo(Doctor, "reason", Now));
    }

    [Fact]
    public void R_T6_amend_needs_the_special_right_and_a_reason_and_returns_to_Complete()
    {
        var result = Verified();
        Refused(ResultsCodes.NoModifyVerifiedRight, () => result.Amend(ResultValueInput.Number(4.6m), Numeric1, Potassium, null, With(rights => rights with { CanModifyVerified = false }), true, "typo", Now));
        Refused(ResultsCodes.ReasonRequired, () => result.Amend(ResultValueInput.Number(4.6m), Numeric1, Potassium, null, Doctor, true, "", Now));
        Refused(ResultsCodes.NotAmendable, () => Complete().Amend(ResultValueInput.Number(4.6m), Numeric1, Potassium, null, Doctor, true, "typo", Now));

        result.MarkPrinted(Doctor, false, null, Now);
        result.Amend(ResultValueInput.Number(4.6m), Numeric1, Potassium, null, Doctor, true, "Transcription error.", Now.AddMinutes(1));

        Assert.Equal((ResultStatus.Complete, 4.6m, 1, 1), (result.Status, result.NumericValue, result.AmendmentCount, result.PrintCount));
        Assert.Null(result.VerifiedByUserId);
        Assert.Contains(result.Log, row => row.Kind == ResultLogKind.Value && row.ValueBefore == "4.2" && row.ValueAfter == "4.6" && row.Detail == "Transcription error.");
        Assert.Contains(result.DomainEvents, e => e is ResultAmended amended && amended.Reason == "Transcription error.");
    }

    [Fact]
    public void R_T8_print_only_signed_results_unless_the_tenant_allows_it()
    {
        var result = Verified();
        result.MarkPrinted(Doctor, false, null, Now);
        Assert.Equal((ResultStatus.Printed, 1, Now), (result.Status, result.PrintCount, result.FirstPrintedAtUtc!.Value));
        result.MarkPrinted(Doctor, false, null, Now.AddMinutes(5));
        Assert.Equal((ResultStatus.Printed, 2, Now), (result.Status, result.PrintCount, result.FirstPrintedAtUtc!.Value));
        Assert.Contains(result.DomainEvents, e => e is ResultPrinted printed && printed.PrintCount == 2);

        Refused(ResultsCodes.NotPrintable, () => Complete().MarkPrinted(Doctor, false, null, Now));
        Refused(ResultsCodes.NoValue, () => Ordered().MarkPrinted(Doctor, true, null, Now));
        Refused(ResultsCodes.NoPrintRight, () => Verified().MarkPrinted(With(rights => rights with { CanPrint = false }), false, null, Now));

        var preliminary = Complete();
        preliminary.MarkPrinted(Doctor, true, null, Now);
        Assert.Equal((ResultStatus.Complete, true), (preliminary.Status, preliminary.IsPrinted));
    }

    [Fact]
    public void R_T9_cancel_is_idempotent_and_freezes_the_result()
    {
        var result = Complete();
        Assert.True(result.Cancel(Registrar, "Patient left.", Now));
        Assert.False(result.Cancel(Registrar, "again", Now));
        Assert.Equal((ResultStatus.Cancelled, "Patient left."), (result.Status, result.CancellationReason));
        Assert.Contains(result.DomainEvents, e => e is ResultCancelled);
        Refused(ResultsCodes.Cancelled, () => result.Enter(ResultValueInput.Number(1m), Numeric1, Potassium, null, Bench, true, Now));
        Refused(ResultsCodes.Cancelled, () => result.Verify(Doctor, false, Now));
    }

    [Fact]
    public void R_T10_report_comment_locked_once_signed_and_note_always_editable()
    {
        var result = Complete();
        Assert.True(result.SetReportComment(" Repeat advised. ", Bench, Now));
        Assert.False(result.SetReportComment("Repeat advised.", Bench, Now));
        Assert.Equal("Repeat advised.", result.ReportComment);
        Refused(ResultsCodes.CommentTooLong, () => result.SetReportComment(new string('x', Result.MaxCommentLength + 1), Bench, Now));

        result.Verify(Doctor, false, Now);
        Refused(ResultsCodes.CommentLocked, () => result.SetReportComment("Changed", Bench, Now));
        Assert.True(result.SetInternalNote("Called Dr. Samir at 10:05.", Bench, Now));
        Assert.Contains(result.Log, row => row.Kind == ResultLogKind.Note && row.ValueAfter == "Called Dr. Samir at 10:05.");
    }

    [Fact]
    public void R_T12_T13_T14_policies_move_retime_and_follow_the_payer()
    {
        var result = Ordered();
        var survivor = Guid.CreateVersion7();
        Assert.True(result.MoveToPatient(survivor, Registrar, "merge", Now));
        Assert.False(result.MoveToPatient(survivor, Registrar, "merge", Now));
        Assert.Equal(survivor, result.PatientId);

        Assert.True(result.Retime(Now, Now.AddHours(24)));
        Assert.Equal((Now, Now.AddHours(24)), (result.ClockStartUtc, result.ExpectedAtUtc));
        Assert.False(Verified().Retime(Now, Now.AddHours(1)));

        var payer = Guid.CreateVersion7();
        Assert.True(result.FollowPayer(payer));
        Assert.False(result.FollowPayer(payer));
    }

    [Fact]
    public void R_BR04_the_lowest_status_of_the_live_results_is_the_unit_status()
    {
        Assert.Equal(ResultStatus.Complete, ResultStatuses.Lowest([ResultStatus.Verified, ResultStatus.Complete, ResultStatus.Printed, ResultStatus.Cancelled]));
        Assert.Null(ResultStatuses.Lowest([ResultStatus.Cancelled]));
    }

    [Fact]
    public void R_BR10_not_X_is_everything_below_X()
    {
        Assert.Equal([ResultStatus.Ordered, ResultStatus.Complete, ResultStatus.Reviewed], ResultStatuses.Below(ResultStatus.Verified));
        Assert.Equal([ResultStatus.Ordered, ResultStatus.Complete], ResultStatuses.Below(ResultStatus.Reviewed));
        Assert.Equal([ResultStatus.Ordered], ResultStatuses.Below(ResultStatus.Complete));
    }

    [Fact]
    public void Report_unit_keys_carry_the_visit_and_the_print_alone_service()
    {
        var visit = Guid.CreateVersion7();
        var line = Guid.CreateVersion7();
        var service = Guid.CreateVersion7();
        var group = ReportUnits.ForGroup(visit, Guid.CreateVersion7());
        var alone = ReportUnits.ForService(visit, line, service);

        Assert.True(ReportUnits.IsWellFormed(group));
        Assert.True(ReportUnits.IsWellFormed(alone));
        Assert.False(ReportUnits.IsWellFormed("not-a-key"));
        Assert.Equal((visit, visit), (ReportUnits.VisitOf(group)!.Value, ReportUnits.VisitOf(alone)!.Value));
        Assert.Equal(((Guid?)null, (Guid?)service), (ReportUnits.ServiceOf(group), ReportUnits.ServiceOf(alone)));
        Assert.True(alone.Length <= Result.MaxReportUnitKeyLength);
    }
}
