using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Organization;

/// <summary>BR-17 resolution (intake LIS-INT-ORG-001 §5), INV-ORG-007 and the staff-account rules (INV-ORG-013, U7, U9).</summary>
public sealed class EffectivePrivilegesTests
{
    [Fact]
    public void AS_U4_Effective_keys_are_role_plus_added_minus_removed_within_the_catalogue()
    {
        var keys = EffectivePrivileges.PermissionKeys(
            roleKeys: ["registration.view", "registration.create", "billing.view"],
            addedKeys: ["patients.view", "not.a-key"],
            removedKeys: ["registration.create"],
            platformTenant: false);

        Assert.Equal(["billing.view", "patients.view", "registration.view"], keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Platform_only_keys_resolve_only_in_the_platform_tenant()
    {
        string[] role = ["tenancy.manage", "platform.view", "audit.view"];

        Assert.Equal(["audit.view"], EffectivePrivileges.PermissionKeys(role, [], [], platformTenant: false));
        Assert.Equal(3, EffectivePrivileges.PermissionKeys(role, [], [], platformTenant: true).Count);
        Assert.True(EffectivePrivileges.IsPlatformOnly("tenancy.view"));
        Assert.False(EffectivePrivileges.IsPlatformOnly("billing.view"));
    }

    [Fact]
    public void The_laboratory_administrator_set_is_the_catalogue_without_the_platform_modules()
    {
        var grantable = EffectivePrivileges.TenantGrantableKeys;

        Assert.DoesNotContain(grantable, key => key.StartsWith("platform.", StringComparison.Ordinal) || key.StartsWith("tenancy.", StringComparison.Ordinal));
        Assert.Contains("organization.manage", grantable);
        Assert.Contains("identity.manage", grantable);
        Assert.Equal(PermissionCatalog.Keys.Count(key => !EffectivePrivileges.IsPlatformOnly(key)), grantable.Count);
    }

    [Fact]
    public void Special_rights_resolve_override_then_role_then_none()
    {
        var role = new SpecialRights(10, true, false, false, false, false, false, false, false);
        var userOverride = new SpecialRights(0, false, true, false, false, false, false, false, false);

        Assert.Same(userOverride, EffectivePrivileges.SpecialRightsOf(role, userOverride));
        Assert.Same(role, EffectivePrivileges.SpecialRightsOf(role, null));
        Assert.Same(SpecialRights.None, EffectivePrivileges.SpecialRightsOf(null, null));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("this.username.is.longer.than.30")]
    [InlineData("has-dash")]
    [InlineData("name@lab")]
    public void AS_U1_A_staff_username_outside_4_to_30_letters_digits_dot_underscore_is_ORG_023(string username)
    {
        var refusal = Assert.Throws<DomainException>(() => StaffAccount.CleanUsername(username));

        Assert.Equal(OrganizationCodes.UsernameInvalid, refusal.Code);
    }

    [Fact]
    public void A_staff_username_is_trimmed_and_lower_cased()
    {
        Assert.Equal("mona.adel_2", StaffAccount.CleanUsername("  Mona.Adel_2 "));
    }

    [Fact]
    public void AS_U3_An_administrator_reset_sets_must_change_rotates_the_stamp_and_clears_an_automatic_lockout()
    {
        var user = PlatformUser.Register("mona.adel", "mona@lab.example", "Mona", "hash-1", FixedClock.Instant);
        user.RecordFailedPasswordCheck(FixedClock.Instant, 1, TimeSpan.FromMinutes(30));
        var stamp = user.SecurityStamp;
        user.ClearDomainEvents();

        user.ResetPasswordByAdministrator("hash-2", mustChange: true, FixedClock.Instant);

        Assert.Equal("hash-2", user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.False(user.IsLockedOutAt(FixedClock.Instant));
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.True(Assert.IsType<PlatformUserPasswordReset>(Assert.Single(user.DomainEvents)).MustChange);
    }

    [Fact]
    public void AS_U2_An_administrator_lock_has_no_end_survives_a_reset_and_ends_with_unlock()
    {
        var user = PlatformUser.Register("mona.adel", "mona@lab.example", "Mona", "hash-1", FixedClock.Instant);

        user.LockByAdministrator(FixedClock.Instant);
        user.LockByAdministrator(FixedClock.Instant);
        user.ResetPasswordByAdministrator("hash-2", mustChange: false, FixedClock.Instant);

        Assert.True(user.IsLockedByAdministrator);
        Assert.True(user.IsLockedOutAt(FixedClock.Instant.AddYears(5)));
        Assert.Single(user.DomainEvents.OfType<PlatformUserLockedByAdministrator>());

        user.Unlock(FixedClock.Instant);

        Assert.False(user.IsLockedOutAt(FixedClock.Instant));
    }

    [Fact]
    public void A_password_change_clears_the_must_change_flag()
    {
        var user = PlatformUser.Register("mona.adel", "mona@lab.example", "Mona", "hash-1", FixedClock.Instant);
        user.RequirePasswordChange(true);

        user.ChangePassword("hash-2", FixedClock.Instant);

        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public void An_expiry_in_the_past_is_ORG_022_and_a_future_one_expires_at_its_instant()
    {
        var user = PlatformUser.Register("mona.adel", "mona@lab.example", "Mona", "hash-1", FixedClock.Instant);

        Assert.Equal(OrganizationCodes.PasswordExpiryInvalid, Assert.Throws<DomainException>(() => user.SetPasswordExpiry(FixedClock.Instant.AddDays(-1), FixedClock.Instant)).Code);

        user.SetPasswordExpiry(FixedClock.Instant.AddDays(30), FixedClock.Instant);

        Assert.False(user.IsPasswordExpiredAt(FixedClock.Instant.AddDays(29)));
        Assert.True(user.IsPasswordExpiredAt(FixedClock.Instant.AddDays(30)));
    }
}
