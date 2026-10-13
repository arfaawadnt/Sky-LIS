namespace Platform.Domain.Reporting;

/// <summary>
/// How the work amount report (change request 2026-10-13 item 3) groups the live service lines of the visits
/// registered in the period. Every grouping shows the same figures per group: visits, services, gross, discount, cash
/// income (patient shares), credit income (payer shares) and net.
/// </summary>
public enum WorkAmountGrouping
{
    /// <summary>One row per branch (the report covers one branch, so one row plus the total; kept for printing).</summary>
    Branch = 0,

    /// <summary>The user who registered the visit.</summary>
    User = 1,

    /// <summary>The payer of the visit (cash payer, companies, insurers).</summary>
    Payer = 2,

    /// <summary>The contract tier of the visit (payer · tier).</summary>
    ContractTier = 3,

    /// <summary>The referring doctor of the visit; visits without one fall in "no referring doctor".</summary>
    Doctor = 4,

    /// <summary>The laboratory-calendar day the visit was registered on, ascending.</summary>
    Day = 5,

    /// <summary>The clinical group of the service (catalogue); services without one fall in "not grouped".</summary>
    ClinicalGroup = 6,

    /// <summary>The service itself (code and names as snapshotted on the line).</summary>
    Service = 7,
}
