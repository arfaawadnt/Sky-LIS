using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Results;

/// <summary>
/// One row of a result's history (LDM §5.3 "RL" result log and "SL" status log; CP-19 "result log, status log"): a value
/// change with before and after, source and analyser; a status change with from and to; a
/// delta acknowledgement; a comment or note change. Child entity of <see cref="Result"/> (<c>app.result_log_entries</c>,
/// composite FK to the root), appended by the root's transitions only and never changed (INV-RES-010, BR-15).
/// </summary>
public sealed class ResultLogEntry : Entity, ITenantScoped
{
    public const int MaxValueLength = 1000;
    public const int MaxDetailLength = 1000;
    public const int MaxNameLength = 200;

    private ResultLogEntry()
    {
    }

    private ResultLogEntry(Guid resultId, int sequence, ResultLogKind kind, DateTimeOffset atUtc)
        : base(Guid.CreateVersion7())
    {
        ResultId = resultId;
        Sequence = sequence;
        Kind = kind;
        AtUtc = atUtc;
    }

    public Guid TenantId { get; private set; }

    public Guid ResultId { get; private set; }

    /// <summary>1, 2, 3 … in the order the root appended the rows (steps of one transition share their time).</summary>
    public int Sequence { get; private set; }

    public ResultLogKind Kind { get; private set; }

    public ResultStatus? FromStatus { get; private set; }

    public ResultStatus? ToStatus { get; private set; }

    public string? ValueBefore { get; private set; }

    public string? ValueAfter { get; private set; }

    public ResultSource? Source { get; private set; }

    public Guid? AnalyserId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    public Guid? ByUserId { get; private set; }

    /// <summary>The actor's display name as of the action.</summary>
    public string? ByName { get; private set; }

    /// <summary>The reason, the refusal code and message, or the comment of the step.</summary>
    public string? Detail { get; private set; }

    internal static ResultLogEntry Record(
        Guid resultId,
        int sequence,
        ResultLogKind kind,
        DateTimeOffset atUtc,
        Guid? byUserId,
        string? byName,
        ResultStatus? fromStatus = null,
        ResultStatus? toStatus = null,
        string? valueBefore = null,
        string? valueAfter = null,
        ResultSource? source = null,
        Guid? analyserId = null,
        string? detail = null) =>
        new(resultId, sequence, kind, atUtc)
        {
            ByUserId = byUserId is { } user && user != Guid.Empty ? user : null,
            ByName = Clip(byName, MaxNameLength),
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ValueBefore = Clip(valueBefore, MaxValueLength),
            ValueAfter = Clip(valueAfter, MaxValueLength),
            Source = source,
            AnalyserId = analyserId,
            Detail = Clip(detail, MaxDetailLength),
        };

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength - 1), "…");
    }
}
