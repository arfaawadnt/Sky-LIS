namespace Platform.Application.Reporting;

/// <summary>
/// One live (Ordered or Reserved) top-level service line of a visit registered in the window, with the visit's
/// grouping keys. Package member lines carry no amounts of their own (the package line does) and are not read.
/// Amounts as Registration stores them (intake LIS-INT-REG-001 §5): <see cref="PatientShare"/> is net of the line's part
/// of the visit discount, <see cref="PayerShare"/> is never discounted, <see cref="ListPrice"/> is the contract list price.
/// </summary>
public sealed record WorkAmountLine(
    Guid VisitId,
    DateTimeOffset RegisteredAtUtc,
    Guid BranchId,
    Guid RegisteredByUserId,
    Guid PayerId,
    Guid ContractTierId,
    Guid? ReferringDoctorId,
    Guid ServiceId,
    string ServiceCode,
    string ServiceNameEn,
    string ServiceNameAr,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    decimal Discount);

/// <summary>
/// Read port of the work amount report over Registration's tables (<c>app.visits</c>, <c>app.visit_lines</c>, an earlier
/// wave: the adapter uses the entity types). Returns the live top-level lines of the visits of one branch registered in
/// <c>[fromUtc, toUtc)</c>, in registration order; cancelled lines are never returned, so a visit whose services were all
/// cancelled contributes nothing.
/// </summary>
public interface IWorkAmountReader
{
    Task<IReadOnlyList<WorkAmountLine>> ReadAsync(Guid branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

/// <summary>The laboratory and branch names of the report's letterhead (the tenant's client record and the branch; blanks when unknown).</summary>
public interface IReportLetterhead
{
    Task<Contracts.Reporting.WorkAmountLetterhead> GetAsync(Guid branchId, CancellationToken cancellationToken);
}
