using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Integration;

/// <summary>
/// The instrument's code for a catalogue sample type (SRS CP-60; LDM manual §13.6: some instruments refuse an order
/// without it). One per (analyser, sample type) (INV-INT-006); sent to the agent in its configuration and on download
/// orders.
/// </summary>
public sealed class AnalyserSampleCode : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;

    private AnalyserSampleCode()
    {
        Code = string.Empty;
    }

    private AnalyserSampleCode(Guid id, Guid analyserId, Guid sampleTypeId, string code)
        : base(id)
    {
        AnalyserId = analyserId;
        SampleTypeId = sampleTypeId;
        Code = code;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public Guid AnalyserId { get; private set; }

    /// <summary>The catalogue sample type (app.sample_types).</summary>
    public Guid SampleTypeId { get; private set; }

    public string Code { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>S1 Add (<c>integration.edit</c>) on an active analyser; uniqueness and the sample type are the handler's checks.</summary>
    public static AnalyserSampleCode Create(Analyser analyser, Guid sampleTypeId, string code)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        Require(analyser.IsActive, IntegrationCodes.AnalyserInactive, "Sample codes cannot be added to an inactive analyser.", DomainFault.Conflict);
        Require(sampleTypeId != Guid.Empty, IntegrationCodes.SampleCodeInvalid, "A sample type is required.", DomainFault.Validation);
        return new AnalyserSampleCode(Guid.CreateVersion7(), analyser.Id, sampleTypeId, CleanCode(code));
    }

    public void ChangeCode(string code) => Code = CleanCode(code);

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public static string CleanCode(string code) =>
        IntegrationGuard.Required(code, MaxCodeLength, IntegrationCodes.SampleCodeInvalid, "The sample code");
}
