using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Application.Organization.Commands;
using Platform.Application.Organization.Queries;
using Platform.Contracts.Organization;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Infrastructure.Security;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Organization;

/// <summary>The handler-side rules of intake LIS-INT-ORG-001 (INV-ORG-007/011/014, SOD-ORG-001), the privilege holder read by Billing and Results, and shape validation.</summary>
public sealed class OrganizationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("short7!", 8)]
    [InlineData("eight-ch", 9)]
    public void AS_U1_A_password_below_the_tenant_minimum_is_ORG_020(string password, int minLength)
    {
        Assert.Equal(OrganizationCodes.PasswordLength, Assert.Throws<DomainException>(() => PasswordRules.Ensure(password, "mona.adel", minLength)).Code);
    }

    [Fact]
    public void A_password_over_128_characters_is_ORG_020()
    {
        Assert.Equal(OrganizationCodes.PasswordLength, Assert.Throws<DomainException>(() => PasswordRules.Ensure(new string('p', 129), "mona.adel", 8)).Code);
    }

    [Fact]
    public void AS_U1_A_password_containing_the_username_in_any_case_is_ORG_021()
    {
        Assert.Equal(OrganizationCodes.PasswordContainsUsername, Assert.Throws<DomainException>(() => PasswordRules.Ensure("xxMONA.ADELxx", "mona.adel", 8)).Code);
    }

    [Fact]
    public void No_composition_rule_applies_decision_of_2026_10_09()
    {
        PasswordRules.Ensure("aaaaaaaa", "mona.adel", 8);
        PasswordRules.Ensure("only lower case words", "mona.adel", 12);
    }

    [Fact]
    public void AS_U6_Nobody_grants_a_permission_they_do_not_hold_ORG_030()
    {
        var privileges = new UserPrivilegesAccessor();
        privileges.Set(["registration.view", "identity.manage"]);

        OrganizationGuards.EnsureGrantable(privileges, ["registration.view"]);
        var refusal = Assert.Throws<DomainException>(() => OrganizationGuards.EnsureGrantable(privileges, ["registration.view", "billing.approve"]));

        Assert.Equal(OrganizationCodes.GrantBeyondOwnPrivileges, refusal.Code);
        Assert.Equal(DomainFault.Refused, refusal.Fault);
        Assert.Contains("billing.approve", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_only_keys_are_granted_in_the_platform_tenant_only_ORG_033()
    {
        OrganizationGuards.EnsureTenantMayGrant(TenantIds.Platform, ["tenancy.manage"]);

        var refusal = Assert.Throws<DomainException>(() => OrganizationGuards.EnsureTenantMayGrant(Guid.CreateVersion7(), ["audit.view", "tenancy.manage"]));

        Assert.Equal(OrganizationCodes.PlatformPermissionOutsidePlatform, refusal.Code);
    }

    [Fact]
    public void AS_U6_An_administrator_does_not_administer_their_own_access_SOD_ORG_001()
    {
        var actor = Guid.CreateVersion7();

        OrganizationGuards.EnsureNotSelf(actor, Guid.CreateVersion7());
        var refusal = Assert.Throws<DomainException>(() => OrganizationGuards.EnsureNotSelf(actor, actor));

        Assert.Equal(OrganizationCodes.SodActorIsTarget, refusal.Code);
        Assert.Equal(DomainFault.Refused, refusal.Fault);
    }

    [Fact]
    public void The_privilege_holder_exposes_special_and_result_rights_from_the_principal()
    {
        var group = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var principal = Principal() with
        {
            SpecialRights = new SpecialRights(null, true, true, false, false, false, false, false, false),
            ResultRights = [new ResultRight(group, null, true, false, true, false, false)],
        };
        var privileges = new UserPrivilegesAccessor();

        privileges.Set(principal);

        Assert.True(privileges.Has("results.view"));
        Assert.True(privileges.SpecialRights.CanRefund);
        Assert.Equal(20, privileges.SpecialRights.EffectiveDiscountCap(20));
        Assert.True(privileges.CanOnResults(ResultAction.Verify, group, branch));
        Assert.False(privileges.CanOnResults(ResultAction.Print, group, branch));

        privileges.Set(["results.view"]);   // keys only: rights reset (fail closed)

        Assert.Same(SpecialRights.None, privileges.SpecialRights);
        Assert.False(privileges.CanOnResults(ResultAction.See, group, branch));
    }

    [Fact]
    public void A_principal_is_not_live_once_the_access_end_date_passed()
    {
        var principal = Principal() with { PasswordExpiresAtUtc = Now.AddMinutes(1) };

        Assert.True(principal.IsLiveAt(Now));
        Assert.False(principal.IsLiveAt(Now.AddMinutes(1)));
    }

    [Theory]
    [InlineData(false, true, false, null, StaffStatus.Inactive)]
    [InlineData(true, true, true, null, StaffStatus.Locked)]
    [InlineData(true, true, false, -1, StaffStatus.Expired)]
    [InlineData(true, true, false, 1, StaffStatus.Active)]
    [InlineData(true, false, true, -1, StaffStatus.Inactive)]
    public void The_users_register_status_is_inactive_then_locked_then_expired(bool userActive, bool membershipActive, bool locked, int? expiryDays, StaffStatus expected)
    {
        DateTimeOffset? expiry = expiryDays is { } days ? Now.AddDays(days) : null;

        Assert.Equal(expected, StaffStatuses.Of(userActive, membershipActive, locked, expiry, Now));
    }

    [Fact]
    public void Validators_check_shape_only()
    {
        var create = new CreateUserValidator().Validate(new CreateUserCommand("", "", true, "", "", "", null, null, null, null, null, null, null, "fr"));
        var cap = new SetRoleSpecialRightsValidator().Validate(new SetRoleSpecialRightsCommand(Guid.CreateVersion7(), new SpecialRightsDto(101, false, false, false, false, false, false, false, false)));
        var status = new ListUsersValidator().Validate(new ListUsersQuery(null, null, "flying", null, null));
        var rights = new SetRoleResultRightsValidator().Validate(new SetRoleResultRightsCommand(Guid.CreateVersion7(), [new ResultRightDto(Guid.Empty, null, true, false, false, false, false)]));

        Assert.Contains(create.Errors, error => error.PropertyName == nameof(CreateUserCommand.Username));
        Assert.Contains(create.Errors, error => error.PropertyName == nameof(CreateUserCommand.PreferredLanguage));
        Assert.False(cap.IsValid);
        Assert.False(status.IsValid);
        Assert.False(rights.IsValid);
        Assert.True(new ListUsersValidator().Validate(new ListUsersQuery("mona", null, "locked", 1, 50)).IsValid);
    }

    private static SessionPrincipal Principal() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Mona",
        true,
        Guid.CreateVersion7(),
        SessionKind.Browser,
        SessionScope.Full,
        Now.AddHours(1),
        Now.AddHours(8),
        false,
        null,
        true,
        "en",
        "Reception",
        new HashSet<string>(["results.view"], StringComparer.Ordinal),
        [],
        [],
        null);
}
