namespace Platform.Application.Reporting;

/// <summary>
/// The captions of the work amount groups, read from the owning modules' ports (Organization branches and users,
/// Pricing payers and tiers, Catalogue B doctors, Catalogue A services and clinical groups). Unknown ids are absent;
/// the handler captions them with the id so a row is never dropped.
/// </summary>
public interface IWorkAmountNames
{
    Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> BranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> UsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> PayersAsync(IReadOnlyCollection<Guid> payerIds, CancellationToken cancellationToken);

    /// <summary>"Payer · tier" captions.</summary>
    Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> ContractTiersAsync(IReadOnlyCollection<Guid> contractTierIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> DoctorsAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken);

    /// <summary>The clinical group of each service (absent when the service has none or is unknown).</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> ClinicalGroupOfServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> ClinicalGroupsAsync(IReadOnlyCollection<Guid> clinicalGroupIds, CancellationToken cancellationToken);
}
