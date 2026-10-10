using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

/// <summary>The five result privileges of LDM manual §12.4 (<c>S R V E P</c>): strictly about test results, nothing else.</summary>
public enum ResultAction
{
    See = 0,
    Review = 1,
    Verify = 2,
    Edit = 3,
    Print = 4,
}

/// <summary>
/// One result right of a role, as written by the Roles page and read by the Results module (SRS CP-37): on the results
/// of <paramref name="ClinicalGroupId"/> in <paramref name="BranchId"/> (<see langword="null"/> = every branch) the holder
/// may see, review, verify, edit and print as flagged. Rows combine: any matching row that grants an action grants it.
/// </summary>
public sealed record ResultRight(Guid ClinicalGroupId, Guid? BranchId, bool CanSee, bool CanReview, bool CanVerify, bool CanEdit, bool CanPrint)
{
    /// <summary>True when any flag is set; an all-false row grants nothing and is not stored.</summary>
    public bool GrantsAnything => CanSee || CanReview || CanVerify || CanEdit || CanPrint;

    public bool Grants(ResultAction action) => action switch
    {
        ResultAction.See => CanSee,
        ResultAction.Review => CanReview,
        ResultAction.Verify => CanVerify,
        ResultAction.Edit => CanEdit,
        ResultAction.Print => CanPrint,
        _ => false,
    };

    /// <summary>Applies to a result of <paramref name="clinicalGroupId"/> produced in <paramref name="branchId"/>.</summary>
    public bool AppliesTo(Guid clinicalGroupId, Guid branchId) => ClinicalGroupId == clinicalGroupId && (BranchId is null || BranchId == branchId);

    /// <summary>Review, verify, edit and print presuppose seeing the result (INV-ORG-008).</summary>
    public ResultRight Normalised() => this with { CanSee = CanSee || CanReview || CanVerify || CanEdit || CanPrint };

    /// <summary>The decision the Results module needs: does any of <paramref name="rights"/> grant <paramref name="action"/> on this group and branch?</summary>
    public static bool Allows(IEnumerable<ResultRight> rights, ResultAction action, Guid clinicalGroupId, Guid branchId)
    {
        ArgumentNullException.ThrowIfNull(rights);
        return rights.Any(right => right.AppliesTo(clinicalGroupId, branchId) && right.Grants(action));
    }
}

/// <summary>A stored result right of a role (owned collection, table <c>app.role_result_rights</c>). Mutated only through <see cref="Role"/>.</summary>
public sealed class RoleResultRight : Entity
{
    private RoleResultRight()
    {
    }

    private RoleResultRight(Guid id, ResultRight right)
        : base(id)
    {
        Apply(right);
    }

    public Guid ClinicalGroupId { get; private set; }

    /// <summary><see langword="null"/> = every branch of the tenant.</summary>
    public Guid? BranchId { get; private set; }

    public bool CanSee { get; private set; }

    public bool CanReview { get; private set; }

    public bool CanVerify { get; private set; }

    public bool CanEdit { get; private set; }

    public bool CanPrint { get; private set; }

    public ResultRight ToRight() => new(ClinicalGroupId, BranchId, CanSee, CanReview, CanVerify, CanEdit, CanPrint);

    internal static RoleResultRight From(ResultRight right) => new(Guid.CreateVersion7(), right);

    internal void Apply(ResultRight right)
    {
        ClinicalGroupId = right.ClinicalGroupId;
        BranchId = right.BranchId;
        CanSee = right.CanSee;
        CanReview = right.CanReview;
        CanVerify = right.CanVerify;
        CanEdit = right.CanEdit;
        CanPrint = right.CanPrint;
    }
}
