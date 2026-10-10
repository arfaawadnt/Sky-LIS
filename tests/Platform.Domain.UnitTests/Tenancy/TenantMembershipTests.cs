using Platform.Domain.Tenancy;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Tenancy;

public sealed class TenantMembershipTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly LocalizedText Name = new("Mona Adel", "منى عادل");

    [Fact]
    public void A_membership_needs_an_explicit_tenant_and_user()
    {
        Assert.Equal(TenancyCodes.TenantRequired, Assert.Throws<DomainException>(() => TenantMembership.Create(Guid.Empty, User, "E-1", Name, "en", FixedClock.Instant)).Code);
        Assert.Equal(TenancyCodes.UserRequired, Assert.Throws<DomainException>(() => TenantMembership.Create(Tenant, Guid.Empty, "E-1", Name, "en", FixedClock.Instant)).Code);
        Assert.Equal(TenancyCodes.EmployeeCodeInvalid, Assert.Throws<DomainException>(() => TenantMembership.Create(Tenant, User, "  ", Name, "en", FixedClock.Instant)).Code);
        Assert.Equal(TenancyCodes.LanguageInvalid, Assert.Throws<DomainException>(() => TenantMembership.Create(Tenant, User, "E-1", Name, "fr", FixedClock.Instant)).Code);
    }

    [Fact]
    public void Creation_records_the_tenant_normalises_inputs_and_raises_one_event()
    {
        var membership = TenantMembership.Create(Tenant, User, " E-1 ", Name, "AR", FixedClock.Instant);

        Assert.Equal(Tenant, membership.TenantId);
        Assert.Equal("E-1", membership.EmployeeCode);
        Assert.Equal("ar", membership.PreferredLanguage);
        Assert.True(membership.IsActive);
        Assert.Empty(membership.BranchIds);
        var created = Assert.IsType<TenantMembershipCreated>(Assert.Single(membership.DomainEvents));
        Assert.Equal((Tenant, membership.Id, User), (created.TenantId, created.MembershipId, created.PlatformUserId));
    }

    [Fact]
    public void Scope_assignment_drops_empty_and_duplicate_ids()
    {
        var membership = TenantMembership.Create(Tenant, User, "E-1", Name, "en", FixedClock.Instant);
        var branch = Guid.CreateVersion7();

        membership.AssignScope([branch, branch, Guid.Empty], []);

        Assert.Equal([branch], membership.BranchIds);
        Assert.Empty(membership.DepartmentIds);
    }

    [Fact]
    public void Deactivation_and_reactivation_are_idempotent_transitions_with_events()
    {
        var membership = TenantMembership.Create(Tenant, User, "E-1", Name, "en", FixedClock.Instant);
        membership.ClearDomainEvents();

        membership.Deactivate(FixedClock.Instant);
        membership.Deactivate(FixedClock.Instant);
        membership.Reactivate(FixedClock.Instant.AddDays(1));

        Assert.True(membership.IsActive);
        Assert.Equal(2, membership.DomainEvents.Count);
        Assert.IsType<TenantMembershipDeactivated>(membership.DomainEvents.First());
        Assert.IsType<TenantMembershipReactivated>(membership.DomainEvents.Last());
    }
}
