using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A diagnosis with its ICD code and the services it adds at registration (SRS CP-49; LDM §3.8 <c>Diagnosis</c>:
/// "IDC Code, Diagnosis Name, tests grid").
/// </summary>
public sealed class Diagnosis : CatalogueBRecord
{
    private Diagnosis()
    {
        IcdCode = string.Empty;
        Name = null!;
        ServiceIds = [];
    }

    private Diagnosis(Guid id, string icdCode)
        : base(id)
    {
        IcdCode = icdCode;
        Name = null!;
        ServiceIds = [];
    }

    /// <summary>ICD-10 code (for example <c>E11.9</c>); unique per tenant, immutable.</summary>
    public string IcdCode { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>The services registration adds when the diagnosis is chosen, in order.</summary>
    public Guid[] ServiceIds { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static Diagnosis Create(string icdCode, LocalizedText name, IEnumerable<Guid>? serviceIds, DateTimeOffset nowUtc)
    {
        var diagnosis = new Diagnosis(Guid.CreateVersion7(), CatalogueBGuard.Code(icdCode));
        diagnosis.Update(name, serviceIds);
        diagnosis.Raise(new DiagnosisCreated(diagnosis.Id, diagnosis.IcdCode, diagnosis.Name.En, nowUtc));
        return diagnosis;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Update(LocalizedText name, IEnumerable<Guid>? serviceIds)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        ServiceIds = CatalogueBGuard.Ids(serviceIds);
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new DiagnosisDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new DiagnosisReactivated(Id, nowUtc);
}
