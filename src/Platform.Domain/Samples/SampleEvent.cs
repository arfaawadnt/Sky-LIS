using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Samples;

/// <summary>
/// One step of a sample's timeline (CP-17, LDM §6 "S. T."): what happened, the resulting status, when, by whom and
/// where. Child entity of <see cref="Sample"/> (<c>app.sample_events</c>, composite FK to the root); appended by the
/// root's transitions only and never changed afterwards (INV-SMP-007, BR-15).
/// </summary>
public sealed class SampleEvent : Entity, ITenantScoped
{
    private SampleEvent()
    {
    }

    private SampleEvent(Guid sampleId, int sequence, SampleAction action, SampleStatus status, DateTimeOffset atUtc, Guid? byUserId, Guid? branchId, Guid? reasonId, string? note)
        : base(Guid.CreateVersion7())
    {
        SampleId = sampleId;
        Sequence = sequence;
        Action = action;
        Status = status;
        AtUtc = atUtc;
        ByUserId = byUserId;
        BranchId = branchId;
        ReasonId = reasonId;
        Note = note;
    }

    public Guid TenantId { get; private set; }

    public Guid SampleId { get; private set; }

    /// <summary>1, 2, 3 … per sample in the order the steps happened: the timeline order, also when several steps share an instant (auto-collect and auto-receive at registration).</summary>
    public int Sequence { get; private set; }

    public SampleAction Action { get; private set; }

    /// <summary>The status of the sample after the step.</summary>
    public SampleStatus Status { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    public Guid? ByUserId { get; private set; }

    /// <summary>The location of the tube at this step (the session branch of the actor, or the visit branch for system steps).</summary>
    public Guid? BranchId { get; private set; }

    public Guid? ReasonId { get; private set; }

    public string? Note { get; private set; }

    internal static SampleEvent Record(Guid sampleId, int sequence, SampleAction action, SampleStatus status, DateTimeOffset atUtc, Guid? byUserId, Guid? branchId, Guid? reasonId = null, string? note = null) =>
        new(sampleId, sequence, action, status, atUtc, byUserId is { } user && user != Guid.Empty ? user : null, branchId is { } branch && branch != Guid.Empty ? branch : null, reasonId, note);
}
