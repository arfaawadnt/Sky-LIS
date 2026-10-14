using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Organization;

public sealed class RoleTests
{
    private static readonly LocalizedText Name = new("Cashier", "أمين صندوق");

    [Fact]
    public void A_role_keeps_catalogued_keys_only_sorted_and_distinct()
    {
        var role = Role.Create(Name, [" treasury.create", "treasury.view", "treasury.view"], FixedClock.Instant);

        Assert.Equal(["treasury.create", "treasury.view"], role.PermissionKeys);
        Assert.True(role.IsActive);
        Assert.IsType<RoleCreated>(Assert.Single(role.DomainEvents));
    }

    [Fact]
    public void An_unknown_permission_is_a_validation_refusal()
    {
        var refusal = Assert.Throws<DomainException>(() => Role.Create(Name, ["treasury.fly"], FixedClock.Instant));

        Assert.Equal(OrganizationCodes.UnknownPermission, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void A_result_right_is_granted_once_and_an_existing_row_is_left_as_set()
    {
        var group = Guid.CreateVersion7();
        var role = Role.Create(Name, ["organization.manage"], FixedClock.Instant);
        role.ClearDomainEvents();

        Assert.True(role.GrantResultRight(ResultRight.Full(group), FixedClock.Instant));
        Assert.False(role.GrantResultRight(ResultRight.Full(group), FixedClock.Instant));   // a retried policy run
        var changed = Assert.IsType<RoleChanged>(Assert.Single(role.DomainEvents));
        Assert.Equal(RoleChangeAspects.ResultRights, changed.Aspect);
        Assert.Equal(new ResultRight(group, null, true, true, true, true, true), Assert.Single(role.ResultRightValues()));

        var seeOnly = Guid.CreateVersion7();
        role.SetResultRights([ResultRight.Full(group), new ResultRight(seeOnly, null, true, false, false, false, false)], FixedClock.Instant);
        Assert.False(role.GrantResultRight(ResultRight.Full(seeOnly), FixedClock.Instant));
        Assert.Equal(new ResultRight(seeOnly, null, true, false, false, false, false), role.ResultRightValues().Single(right => right.ClinicalGroupId == seeOnly));

        var invalid = Assert.Throws<DomainException>(() => role.GrantResultRight(new ResultRight(Guid.Empty, null, true, true, true, true, true), FixedClock.Instant));
        Assert.Equal(OrganizationCodes.ResultRightInvalid, invalid.Code);
    }

    [Fact]
    public void A_role_manages_roles_when_it_holds_organization_manage()
    {
        Assert.True(Role.Create(Name, ["organization.manage"], FixedClock.Instant).ManagesRoles);
        Assert.False(Role.Create(Name, ["organization.view", "organization.edit", "results.approve"], FixedClock.Instant).ManagesRoles);
    }

    [Fact]
    public void A_membership_takes_a_role_of_its_own_active_tenant_only()
    {
        var tenant = Guid.CreateVersion7();
        var membership = TenantMembership.Create(tenant, Guid.CreateVersion7(), "E-1", new LocalizedText("Mona", "منى"), "en", FixedClock.Instant);
        var role = Role.Create(Name, ["treasury.view"], FixedClock.Instant);

        var crossTenant = Assert.Throws<DomainException>(() => membership.AssignRole(role, FixedClock.Instant));
        Assert.Equal(TenancyCodes.RoleOfAnotherTenant, crossTenant.Code);
        Assert.Null(membership.RoleId);
    }
}
