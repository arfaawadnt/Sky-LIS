using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Platform.Domain.Results;

namespace Platform.Infrastructure.Persistence.Results;

/// <summary>
/// <c>app.results</c> (MODULES.md §5 table contract: <c>id, tenant_id, visit_id, accession, line_id, service_id, test_id,
/// tube_id, clinical_group_id, patient_id, branch_id, report_unit_key, status, numeric_value, text_value, flag, is_panic,
/// delta_flag, source, analyser_id, entered_at_utc, reviewed_at_utc, verified_at_utc, verified_by_user_id, printed_at_utc,
/// expected_at_utc</c> are read by other modules): key <c>(tenant_id, id)</c> by convention, one result per line and
/// test (INV-RES-001), the resolved range as owned columns, formatted text and culture as jsonb, RLS declared in the
/// creating migration.
/// </summary>
internal sealed class ResultConfiguration : IEntityTypeConfiguration<Result>
{
    public void Configure(EntityTypeBuilder<Result> builder)
    {
        builder.ToTable("results", AppDbContext.AppSchema);

        builder.Property(result => result.VisitId).IsRequired();
        builder.Property(result => result.Accession).HasMaxLength(Result.MaxAccessionLength).IsRequired();
        builder.Property(result => result.LineId).IsRequired();
        builder.Property(result => result.LineServiceId).IsRequired();
        builder.Property(result => result.ServiceId).IsRequired();
        builder.Property(result => result.TestId).IsRequired();
        builder.Property(result => result.TestType).IsRequired();
        builder.Property(result => result.ClinicalGroupId).IsRequired();
        builder.Property(result => result.ReportUnitKey).HasMaxLength(Result.MaxReportUnitKeyLength).IsRequired();
        builder.Property(result => result.PatientId).IsRequired();
        builder.Property(result => result.BranchId).IsRequired();
        builder.Property(result => result.Status).IsRequired();
        builder.Property(result => result.Source).IsRequired();
        builder.Property(result => result.NumericValue).HasPrecision(18, 6);
        builder.Property(result => result.TextValue).HasMaxLength(Result.MaxTextLength);
        builder.Property(result => result.Unit).HasMaxLength(Result.MaxUnitLength);
        builder.Property(result => result.DeltaPercent).HasPrecision(8, 1);
        builder.Property(result => result.PreviousValue).HasPrecision(18, 6);
        builder.Property(result => result.DeltaComment).HasMaxLength(Result.MaxReasonLength);
        builder.Property(result => result.AnalyserCode).HasMaxLength(Result.MaxAnalyserCodeLength);
        builder.Property(result => result.AnalyserFlags).HasMaxLength(Result.MaxAnalyserFlagsLength);
        builder.Property(result => result.VerifiedByName).HasMaxLength(Result.MaxNameLength);
        builder.Property(result => result.FirstPrintedAtUtc).HasColumnName("printed_at_utc");
        builder.Property(result => result.LastPrintListNumber).HasMaxLength(Result.MaxPrintListNumberLength);
        builder.Property(result => result.CancellationReason).HasMaxLength(Result.MaxReasonLength);
        builder.Property(result => result.ReportComment).HasMaxLength(Result.MaxCommentLength);
        builder.Property(result => result.InternalNote).HasMaxLength(Result.MaxNoteLength);

        builder.Property(result => result.Formatted)
            .HasColumnName("formatted_report")
            .HasColumnType("jsonb")
            .HasConversion(ResultJson.FormattedConverter, ResultJson.FormattedComparer);
        builder.Property(result => result.Culture)
            .HasColumnName("culture_report")
            .HasColumnType("jsonb")
            .HasConversion(ResultJson.CultureConverter, ResultJson.CultureComparer);

        builder.OwnsOne(result => result.Range, range =>
        {
            range.Property(value => value.Low).HasColumnName("range_low").HasPrecision(18, 6);
            range.Property(value => value.High).HasColumnName("range_high").HasPrecision(18, 6);
            range.Property(value => value.PanicLow).HasColumnName("range_panic_low").HasPrecision(18, 6);
            range.Property(value => value.PanicHigh).HasColumnName("range_panic_high").HasPrecision(18, 6);
            range.Property(value => value.ReportableLow).HasColumnName("range_reportable_low").HasPrecision(18, 6);
            range.Property(value => value.ReportableHigh).HasColumnName("range_reportable_high").HasPrecision(18, 6);
            range.Property(value => value.Text).HasColumnName("range_text").HasMaxLength(ResolvedRange.MaxTextLength);
            range.Ignore(value => value.HasLimits);
        });

        builder.HasMany(result => result.Log)
            .WithOne()
            .HasForeignKey(entry => new { entry.TenantId, entry.ResultId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(result => result.Log).HasField("_log").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(result => new { result.TenantId, result.LineId, result.TestId }).IsUnique();
        builder.HasIndex(result => new { result.TenantId, result.VisitId });
        builder.HasIndex(result => new { result.TenantId, result.Accession });
        builder.HasIndex(result => new { result.TenantId, result.TubeId });
        builder.HasIndex(result => new { result.TenantId, result.ReportUnitKey });
        builder.HasIndex(result => new { result.TenantId, result.PatientId, result.TestId });
        builder.HasIndex(result => new { result.TenantId, result.Status, result.ExpectedAtUtc });
        builder.HasIndex(result => new { result.TenantId, result.ClinicalGroupId, result.Status });
        builder.HasIndex(result => new { result.TenantId, result.RegisteredAtUtc });

        builder.Ignore(result => result.HasValue);
        builder.Ignore(result => result.IsPrinted);
        builder.Ignore(result => result.IsLive);
    }
}

/// <summary><c>app.result_log_entries</c>: the value and status history (CP-19 RL / SL), keyed <c>(tenant_id, id)</c>, composite FK to the result; append-only (trigger in Results.migration.sql).</summary>
internal sealed class ResultLogEntryConfiguration : IEntityTypeConfiguration<ResultLogEntry>
{
    public void Configure(EntityTypeBuilder<ResultLogEntry> builder)
    {
        builder.ToTable("result_log_entries", AppDbContext.AppSchema);

        builder.Property(entry => entry.ResultId).IsRequired();
        builder.Property(entry => entry.Kind).IsRequired();
        builder.Property(entry => entry.AtUtc).IsRequired();
        builder.Property(entry => entry.ValueBefore).HasMaxLength(ResultLogEntry.MaxValueLength);
        builder.Property(entry => entry.ValueAfter).HasMaxLength(ResultLogEntry.MaxValueLength);
        builder.Property(entry => entry.ByName).HasMaxLength(ResultLogEntry.MaxNameLength);
        builder.Property(entry => entry.Detail).HasMaxLength(ResultLogEntry.MaxDetailLength);

        builder.HasIndex(entry => new { entry.TenantId, entry.ResultId, entry.AtUtc });
    }
}

/// <summary>The jsonb shapes of formatted text and culture reports (System.Text.Json, camelCase); compared by their JSON text.</summary>
internal static class ResultJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<FormattedReport?, string?> FormattedConverter = new(report => FormattedText(report), json => RestoreFormatted(json));

    public static readonly ValueComparer<FormattedReport?> FormattedComparer = new(
        (left, right) => FormattedText(left) == FormattedText(right),
        report => HashOf(FormattedText(report)),
        report => report);

    public static readonly ValueConverter<CultureReport?, string?> CultureConverter = new(report => CultureText(report), json => RestoreCulture(json));

    public static readonly ValueComparer<CultureReport?> CultureComparer = new(
        (left, right) => CultureText(left) == CultureText(right),
        report => HashOf(CultureText(report)),
        report => report);

    public static string? FormattedText(FormattedReport? report) => report is null ? null : JsonSerializer.Serialize(report.Paragraphs, Options);

    public static string? CultureText(CultureReport? report) => report is null ? null : JsonSerializer.Serialize(new CultureJson(report.NoGrowth, report.Isolates.ToList()), Options);

    public static FormattedReport? RestoreFormatted(string? json) => json is null ? null : FormattedReport.Restore(JsonSerializer.Deserialize<List<FormattedParagraph>>(json, Options));

    public static CultureReport? RestoreCulture(string? json)
    {
        if (json is null)
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<CultureJson>(json, Options);
        return CultureReport.Restore(stored?.NoGrowth ?? false, stored?.Isolates);
    }

    private static int HashOf(string? text) => text is null ? 0 : text.GetHashCode(StringComparison.Ordinal);

    private sealed record CultureJson(bool NoGrowth, List<CultureIsolate>? Isolates);
}
