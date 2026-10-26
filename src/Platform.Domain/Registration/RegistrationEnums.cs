namespace Platform.Domain.Registration;

/// <summary>The visit's state (intake §6): Open while work goes on; Completed is set by later policies (results printed, balance settled).</summary>
public enum VisitStatus
{
    Open = 0,
    Completed = 1,
}

/// <summary>A line's state (BR-05, BR-15): Ordered (billed, tube, results), Reserved (billed, no tube yet), Cancelled (kept with reason).</summary>
public enum VisitLineStatus
{
    Ordered = 0,
    Reserved = 1,
    Cancelled = 2,
}

/// <summary>
/// What a line stands for: an ordered service, a package (one price, several services), one member service of a
/// package (price 0; carries the tube and the tests of that member, intake Q2) or a service ordered for an operand of a
/// formula test (LIS-CR-015; price 0, follows its formula line).
/// </summary>
public enum VisitLineKind
{
    Service = 0,
    Package = 1,
    PackageMember = 2,
    FormulaOperand = 3,
}

/// <summary>Why a line is on the visit: typed by the clerk, added by a test rule, added by the visit's diagnosis, or added for a formula (LIS-CR-015).</summary>
public enum VisitLineOrigin
{
    Manual = 0,
    Rule = 1,
    Diagnosis = 2,
    Formula = 3,
}

/// <summary>Where the patient was registered (CP-06 location type; R1 has no hospital locations).</summary>
public enum VisitLocationType
{
    Walkin = 0,
    Home = 1,
    Batch = 2,
    LabToLab = 3,
}

/// <summary>How the clerk expressed the discount; the amount is always derived from it (intake §5 "Discount").</summary>
public enum DiscountKind
{
    Amount = 0,
    Percent = 1,
}

/// <summary>A logged money transfer (CP-25): the whole visit to another contract, or a shift between the shares of one line.</summary>
public enum VisitTransferKind
{
    Contract = 0,
    LineShares = 1,
}

/// <summary>Home reservation state (CP-07): Booked until converted into a visit or cancelled.</summary>
public enum BookingStatus
{
    Booked = 0,
    Converted = 1,
    Cancelled = 2,
}

/// <summary>Batch kinds (CP-08 group on one contract, CP-09 samples from a sending laboratory).</summary>
public enum BatchKind
{
    Group = 0,
    LabToLab = 1,
}
