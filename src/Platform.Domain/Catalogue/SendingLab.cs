using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A contracted laboratory that sends samples (SRS CP-50; LDM §4.9 lab-to-lab: lab, courier/representative,
/// contract, phone, doctor). Choosing it at lab-to-lab registration fills the payer, tier, courier, phone and doctor.
/// The tier must belong to the payer (INV-CTB-008, checked by the handler against the Pricing tables).
/// </summary>
public sealed class SendingLab : CatalogueBRecord
{
    private SendingLab()
    {
        Name = null!;
        Courier = null!;
    }

    private SendingLab(Guid id)
        : base(id)
    {
        Name = null!;
        Courier = null!;
    }

    /// <summary>English name unique per tenant (INV-CTB-003).</summary>
    public LocalizedText Name { get; private set; }

    /// <summary>The payer (Pricing) the lab's visits are billed to.</summary>
    public Guid PayerId { get; private set; }

    /// <summary>The contract tier (Pricing) of <see cref="PayerId"/> that prices the lab's visits.</summary>
    public Guid ContractTierId { get; private set; }

    /// <summary>The courier (representative) who brings the samples.</summary>
    public LocalizedText Courier { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>The default referring doctor of the lab's visits.</summary>
    public Guid? DoctorId { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>). A linked doctor must be active (CAT-310).</summary>
    public static SendingLab Create(LocalizedText name, Guid payerId, Guid contractTierId, LocalizedText courier, string? phone, Doctor? doctor, DateTimeOffset nowUtc)
    {
        var lab = new SendingLab(Guid.CreateVersion7());
        lab.Update(name, payerId, contractTierId, courier, phone, doctor);
        lab.Raise(new SendingLabCreated(lab.Id, lab.PayerId, lab.ContractTierId, lab.Name.En, nowUtc));
        return lab;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>). A newly linked doctor must be active; an unchanged link stays valid.</summary>
    public void Update(LocalizedText name, Guid payerId, Guid contractTierId, LocalizedText courier, string? phone, Doctor? doctor)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(courier);
        Require(payerId != Guid.Empty, CatalogueBCodes.PayerUnknown, "A sending lab needs a payer.", DomainFault.Validation);
        Require(contractTierId != Guid.Empty, CatalogueBCodes.ContractTierUnknown, "A sending lab needs a contract tier.", DomainFault.Validation);
        if (doctor is not null && doctor.Id != DoctorId)
        {
            RequireActiveLink(doctor.IsActive, "doctor");
        }

        Name = name;
        PayerId = payerId;
        ContractTierId = contractTierId;
        Courier = courier;
        Phone = CatalogueBGuard.Phone(phone);
        DoctorId = doctor?.Id;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new SendingLabDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new SendingLabReactivated(Id, nowUtc);
}
