using Platform.Domain.Organization;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Organization;

/// <summary>Acceptance scenarios AS-R1…AS-R4 of intake LIS-INT-ORG-001 §16 (role transitions R1–R7) and the invariants they guard.</summary>
public sealed class RolePrivilegeTests
{
    private static readonly LocalizedText Name = new("Reception", "الاستقبال");
    private static readonly Guid Chemistry = Guid.CreateVersion7();
    private static readonly Guid Haematology = Guid.CreateVersion7();
    private static readonly Guid MainBranch = Guid.CreateVersion7();

    private static Role Reception() => Role.Create(Name, ["registration.create", "registration.view"], FixedClock.Instant, " Front desk ");

    [Fact]
    public void AS_R1_Create_keeps_the_description_and_grants_no_special_right_by_default()
    {
        var role = Reception();

        Assert.Equal("Front desk", role.Description);
        Assert.Equal(SpecialRights.None, role.SpecialRights);
        Assert.Empty(role.ResultRights);
        Assert.True(role.IsActive);
    }

    [Fact]
    public void AS_R1_A_role_without_permissions_is_ORG_005()
    {
        var refusal = Assert.Throws<DomainException>(() => Role.Create(Name, [" "], FixedClock.Instant));

        Assert.Equal(OrganizationCodes.PermissionRequired, refusal.Code);
    }

    [Fact]
    public void R2_A_description_over_500_characters_is_ORG_004()
    {
        var role = Reception();

        var refusal = Assert.Throws<DomainException>(() => role.Describe(Name, new string('x', Role.MaxDescriptionLength + 1)));

        Assert.Equal(OrganizationCodes.DescriptionTooLong, refusal.Code);
    }

    [Fact]
    public void R3_Setting_permissions_raises_RoleChanged_once_and_unchanged_keys_raise_nothing()
    {
        var role = Reception();
        role.ClearDomainEvents();

        role.SetPermissions(["registration.view", "patients.view"], FixedClock.Instant);
        role.SetPermissions(["patients.view", "registration.view"], FixedClock.Instant);

        Assert.Equal(["patients.view", "registration.view"], role.PermissionKeys);
        var changed = Assert.IsType<RoleChanged>(Assert.Single(role.DomainEvents));
        Assert.Equal(RoleChangeAspects.Permissions, changed.Aspect);
    }

    [Fact]
    public void R3_An_active_role_cannot_drop_its_last_permission_ORG_005()
    {
        var role = Reception();

        var refusal = Assert.Throws<DomainException>(() => role.SetPermissions([], FixedClock.Instant));

        Assert.Equal(OrganizationCodes.PermissionRequired, refusal.Code);
        Assert.Equal(2, role.PermissionKeys.Length);
    }

    [Fact]
    public void AS_R2_Special_rights_replace_the_value_and_a_cap_over_100_is_ORG_006()
    {
        var role = Reception();
        role.ClearDomainEvents();
        var rights = new SpecialRights(15, true, false, false, true, false, false, true, false);

        role.SetSpecialRights(rights, FixedClock.Instant);
        role.SetSpecialRights(new SpecialRights(15, true, false, false, true, false, false, true, false), FixedClock.Instant);

        Assert.Equal(rights, role.SpecialRights);
        Assert.NotSame(rights, role.SpecialRights);   // an owned value is never shared
        Assert.Equal(RoleChangeAspects.SpecialRights, Assert.IsType<RoleChanged>(Assert.Single(role.DomainEvents)).Aspect);
        Assert.Equal(OrganizationCodes.DiscountCapInvalid, Assert.Throws<DomainException>(() => new SpecialRights(101, false, false, false, false, false, false, false, false)).Code);
        Assert.Equal(OrganizationCodes.DiscountCapInvalid, Assert.Throws<DomainException>(() => new SpecialRights(-1, false, false, false, false, false, false, false, false)).Code);
    }

    [Fact]
    public void A_null_cap_defers_to_the_tenant_default_and_zero_means_no_discount()
    {
        Assert.Equal(20, new SpecialRights(null, false, false, false, false, false, false, false, false).EffectiveDiscountCap(20));
        Assert.Equal(0, SpecialRights.None.EffectiveDiscountCap(20));
        Assert.Equal(100, SpecialRights.All.EffectiveDiscountCap(20));
    }

    [Fact]
    public void AS_R3_Result_rights_are_normalised_deduplicated_by_scope_and_updated_in_place()
    {
        var role = Reception();
        role.ClearDomainEvents();

        role.SetResultRights(
            [
                new ResultRight(Chemistry, null, false, false, true, false, false),        // verify implies see
                new ResultRight(Haematology, MainBranch, true, false, false, false, true),
                new ResultRight(Haematology, null, false, false, false, false, false),     // grants nothing: dropped
            ],
            FixedClock.Instant);

        var rights = role.ResultRightValues();
        Assert.Equal(2, rights.Count);
        Assert.Contains(new ResultRight(Chemistry, null, true, false, true, false, false), rights);
        var chemistryRowId = role.ResultRights.Single(row => row.ClinicalGroupId == Chemistry).Id;

        role.SetResultRights([new ResultRight(Chemistry, null, true, true, true, true, true)], FixedClock.Instant);

        var row = Assert.Single(role.ResultRights);
        Assert.Equal(chemistryRowId, row.Id);   // same scope: updated in place
        Assert.True(row.CanPrint);
        Assert.Equal(2, role.DomainEvents.Count);
        Assert.All(role.DomainEvents, raised => Assert.Equal(RoleChangeAspects.ResultRights, Assert.IsType<RoleChanged>(raised).Aspect));
    }

    [Fact]
    public void AS_R3_A_duplicate_scope_or_a_missing_group_is_ORG_007()
    {
        var role = Reception();

        var duplicate = Assert.Throws<DomainException>(() => role.SetResultRights(
            [new ResultRight(Chemistry, null, true, false, false, false, false), new ResultRight(Chemistry, null, false, true, false, false, false)], FixedClock.Instant));
        var missingGroup = Assert.Throws<DomainException>(() => role.SetResultRights([new ResultRight(Guid.Empty, null, true, false, false, false, false)], FixedClock.Instant));

        Assert.Equal(OrganizationCodes.ResultRightInvalid, duplicate.Code);
        Assert.Equal(OrganizationCodes.ResultRightInvalid, missingGroup.Code);
        Assert.Empty(role.ResultRights);
    }

    [Fact]
    public void Result_rights_combine_all_branch_and_branch_rows()
    {
        var otherBranch = Guid.CreateVersion7();
        ResultRight[] rights =
        [
            new(Chemistry, null, true, false, false, false, false),
            new(Chemistry, MainBranch, true, false, true, false, false),
        ];

        Assert.True(ResultRight.Allows(rights, ResultAction.See, Chemistry, otherBranch));
        Assert.True(ResultRight.Allows(rights, ResultAction.Verify, Chemistry, MainBranch));
        Assert.False(ResultRight.Allows(rights, ResultAction.Verify, Chemistry, otherBranch));
        Assert.False(ResultRight.Allows(rights, ResultAction.See, Haematology, MainBranch));
    }

    [Fact]
    public void AS_R4_A_role_with_members_cannot_be_deactivated_ORG_011()
    {
        var role = Reception();

        var refusal = Assert.Throws<DomainException>(() => role.Deactivate(3, FixedClock.Instant));

        Assert.Equal(OrganizationCodes.RoleHasMembers, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
        Assert.True(role.IsActive);
    }

    [Fact]
    public void AS_R4_Deactivate_without_members_then_activate_raise_the_matching_aspects()
    {
        var role = Reception();
        role.ClearDomainEvents();

        role.Deactivate(0, FixedClock.Instant);
        role.Deactivate(0, FixedClock.Instant);
        role.Reactivate(FixedClock.Instant);

        Assert.True(role.IsActive);
        Assert.Equal([RoleChangeAspects.Deactivated, RoleChangeAspects.Activated], role.DomainEvents.Cast<RoleChanged>().Select(raised => raised.Aspect));
    }

    [Fact]
    public void An_inactive_role_may_be_emptied_but_not_activated_without_a_permission_ORG_005()
    {
        var role = Reception();
        role.Deactivate(0, FixedClock.Instant);
        role.SetPermissions([], FixedClock.Instant);

        var refusal = Assert.Throws<DomainException>(() => role.Reactivate(FixedClock.Instant));

        Assert.Equal(OrganizationCodes.PermissionRequired, refusal.Code);
        Assert.False(role.IsActive);
    }

    [Fact]
    public void A_provisioned_role_carries_its_tenant_before_any_commit()
    {
        var tenant = Guid.CreateVersion7();

        var role = Role.CreateForTenant(tenant, Name, EffectivePrivileges.TenantGrantableKeys, FixedClock.Instant, null, SpecialRights.All);

        Assert.Equal(tenant, role.TenantId);
        Assert.Equal(SpecialRights.All, role.SpecialRights);
        Assert.DoesNotContain(role.PermissionKeys, EffectivePrivileges.IsPlatformOnly);
    }
}
