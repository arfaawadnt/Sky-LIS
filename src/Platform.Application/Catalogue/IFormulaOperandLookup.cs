namespace Platform.Application.Catalogue;

/// <summary>
/// The operands of the formula tests a set of services carries (change request LIS-CR-015, package P-FCT owns the adapter,
/// P-FRG consumes it at registration): for each given service, every formula test among its members (a test service's own test,
/// a profile's tests; a package's member services must be passed themselves) and, per referenced test of that formula, the
/// service the laboratory chose to order for it and whether that line shows on the receipt. Inactive formula tests and services
/// are included (the caller decides on activity, BR-24). Reads are bounded to the current tenant.
/// </summary>
public interface IFormulaOperandLookup
{
    Task<IReadOnlyList<CatalogueFormulaOperandInfo>> ForServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);
}

/// <summary>
/// One operand of one formula test carried by <paramref name="ServiceId"/>: the referenced test (<paramref name="OperandTestId"/>,
/// <paramref name="OperandTestCode"/>), the service ordered for it when the formula is registered (<paramref name="OperandServiceId"/>;
/// null = the laboratory chose none, nothing is added) and whether that added line shows on the receipt.
/// </summary>
public sealed record CatalogueFormulaOperandInfo(
    Guid ServiceId,
    Guid FormulaTestId,
    Guid OperandTestId,
    string OperandTestCode,
    Guid? OperandServiceId,
    bool VisibleOnReceipt);
