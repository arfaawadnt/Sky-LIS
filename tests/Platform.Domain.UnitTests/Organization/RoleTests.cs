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
