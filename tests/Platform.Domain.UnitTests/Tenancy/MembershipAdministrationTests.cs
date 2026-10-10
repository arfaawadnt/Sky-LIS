using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Tenancy;

/// <summary>Acceptance scenarios AS-U4 and AS-U5 of intake LIS-INT-ORG-001 §16 (transitions U2–U6, U10) and INV-ORG-010/012.</summary>
public sealed class MembershipAdministrationTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly LocalizedText Name = new("Mona Adel", "منى عادل");

    private static TenantMembership Member() => TenantMembership.Create(Tenant, Guid.CreateVersion7(), "E-1", Name, "en", FixedClock.Instant);

    private static Role RoleOfTenant(string nameEn = "Reception") =>
        Role.CreateForTenant(Tenant, new LocalizedText(nameEn, "دور"), ["registration.view"], FixedClock.Instant);

    [Fact]
    public void U2_The_profile_keeps_a_designation_distinct_shifts_and_the_language()
    {
        var membership = Member();

        membership.UpdateProfile(new LocalizedText("Mona A.", "منى ع."), "  Chemist ", ["Morning", "morning", " Night ", ""], "AR");

        Assert.Equal("Chemist", membership.Designation);
        Assert.Equal(["Morning", "Night"], membership.Shifts);
        Assert.Equal("ar", membership.PreferredLanguage);
        Assert.Equal("Mona A.", membership.FullName.En);
    }

    [Fact]
    public void U2_Too_many_shifts_is_TEN_006_and_a_long_designation_is_TEN_005()
    {
        var membership = Member();

        var shifts = Assert.Throws<DomainException>(() => membership.UpdateProfile(Name, null, Enumerable.Range(0, TenantMembership.MaxShifts + 1).Select(i => $"S{i}"), "en"));
        var designation = Assert.Throws<DomainException>(() => membership.UpdateProfile(Name, new string('d', TenantMembership.MaxDesignationLength + 1), [], "en"));

        Assert.Equal(TenancyCodes.ShiftsInvalid, shifts.Code);
        Assert.Equal(TenancyCodes.DesignationInvalid, designation.Code);
    }

    [Fact]
    public void U3_Branch_assignment_drops_empty_and_duplicate_ids_and_empty_means_unrestricted()
    {
        var membership = Member();
        var branch = Guid.CreateVersion7();

        membership.AssignBranches([branch, branch, Guid.Empty]);
        Assert.Equal([branch], membership.BranchIds);

        membership.AssignBranches([]);
        Assert.Empty(membership.BranchIds);
    }

    [Fact]
    public void U4_Assigning_the_same_role_again_raises_nothing()
    {
        var membership = Member();
        var role = RoleOfTenant();
        membership.AssignRole(role, FixedClock.Instant);
        membership.ClearDomainEvents();

        membership.AssignRole(role, FixedClock.Instant);

        Assert.Empty(membership.DomainEvents);
    }

    [Fact]
    public void U4_An_inactive_role_is_ORG_010()
    {
        var membership = Member();
        var role = RoleOfTenant();
        role.Deactivate(0, FixedClock.Instant);

        Assert.Equal(OrganizationCodes.RoleInactive, Assert.Throws<DomainException>(() => membership.AssignRole(role, FixedClock.Instant)).Code);
    }

    [Fact]
    public void AS_U4_Overrides_mark_the_member_modified_and_restore_returns_to_the_role()
    {
        var membership = Member();
        membership.AssignRole(RoleOfTenant(), FixedClock.Instant);
        membership.ClearDomainEvents();

        membership.SetOverrides(["patients.view", "patients.view"], ["registration.view"], new SpecialRights(5, true, false, false, false, false, false, false, false), FixedClock.Instant);

        Assert.True(membership.IsModified);
        Assert.Equal(["patients.view"], membership.AddedPermissionKeys);
        Assert.Equal(["registration.view"], membership.RemovedPermissionKeys);
        Assert.Equal(5, membership.SpecialRightsOverride!.DiscountCapPercent);
        var overridden = Assert.IsType<TenantMembershipPrivilegesOverridden>(Assert.Single(membership.DomainEvents));
        Assert.Equal((1, 1, true), (overridden.AddedCount, overridden.RemovedCount, overridden.SpecialRightsOverridden));

        membership.RestoreToRole(FixedClock.Instant);
        membership.RestoreToRole(FixedClock.Instant);

        Assert.False(membership.IsModified);
        Assert.Empty(membership.AddedPermissionKeys);
        Assert.Null(membership.SpecialRightsOverride);
        Assert.Single(membership.DomainEvents.OfType<TenantMembershipRestoredToRole>());
    }

    [Fact]
    public void A_key_both_added_and_removed_is_TEN_013_and_an_unknown_key_is_ORG_001()
    {
        var membership = Member();

        var both = Assert.Throws<DomainException>(() => membership.SetOverrides(["patients.view"], ["patients.view"], null, FixedClock.Instant));
        var unknown = Assert.Throws<DomainException>(() => membership.SetOverrides(["patients.fly"], [], null, FixedClock.Instant));

        Assert.Equal(TenancyCodes.OverrideConflict, both.Code);
        Assert.Equal(OrganizationCodes.UnknownPermission, unknown.Code);
        Assert.False(membership.IsModified);
    }

    [Fact]
    public void AS_U5_The_primary_administrator_is_not_deactivated_demoted_or_stripped_TEN_012()
    {
        var membership = Member();
        membership.MarkPrimaryAdministrator();
        membership.AssignRole(RoleOfTenant("Laboratory Administrator"), FixedClock.Instant);

        var deactivate = Assert.Throws<DomainException>(() => membership.Deactivate(FixedClock.Instant));
        var demote = Assert.Throws<DomainException>(() => membership.AssignRole(RoleOfTenant("Reception"), FixedClock.Instant));
        var strip = Assert.Throws<DomainException>(() => membership.SetOverrides([], ["registration.view"], null, FixedClock.Instant));

        Assert.All([deactivate, demote, strip], refusal => Assert.Equal(TenancyCodes.PrimaryAdministratorProtected, refusal.Code));
        Assert.True(membership.IsActive);

        membership.SetOverrides(["patients.view"], [], null, FixedClock.Instant);   // adding is allowed
        Assert.True(membership.IsModified);
    }

    [Fact]
    public void The_signature_reference_is_set_and_cleared()
    {
        var membership = Member();
        var file = Guid.CreateVersion7();

        membership.SetSignature(file);
        Assert.Equal(file, membership.SignatureFileId);

        membership.SetSignature(Guid.Empty);
        Assert.Null(membership.SignatureFileId);
    }
}
