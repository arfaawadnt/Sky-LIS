using System.Net;
using Platform.Domain.Identity;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Identity;

/// <summary>CP-04: the branch of the session (intake LIS-INT-FND-001, capability 2).</summary>
public sealed class SessionBranchTests
{
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Branch = Guid.CreateVersion7();

    private static byte[] Hash(byte seed) => Enumerable.Repeat(seed, RefreshSession.TokenHashLength).ToArray();

    private static RefreshSession Open() =>
        RefreshSession.Open(User, Tenant, SessionKind.Browser, SessionScope.Full, Hash(1), FixedClock.Instant, TimeSpan.FromHours(8), TimeSpan.FromHours(12), IPAddress.Loopback, "agent");

    [Fact]
    public void A_live_session_selects_a_branch_once_and_announces_it()
    {
        var session = Open();
        session.ClearDomainEvents();

        session.SelectBranch(Branch, FixedClock.Instant);
        session.SelectBranch(Branch, FixedClock.Instant.AddMinutes(1));   // re-selecting the same branch is a no-op

        Assert.Equal(Branch, session.BranchId);
        var selected = Assert.IsType<RefreshSessionBranchSelected>(Assert.Single(session.DomainEvents));
        Assert.Equal((session.Id, User, Tenant, Branch), (selected.SessionId, selected.UserId, selected.TenantId, selected.BranchId));
    }

    [Fact]
    public void Rotation_carries_the_branch_to_the_successor()
    {
        var session = Open();
        session.SelectBranch(Branch, FixedClock.Instant);

        var successor = session.Rotate(Hash(2), FixedClock.Instant.AddMinutes(10), TimeSpan.FromHours(8));

        Assert.Equal(Branch, successor.BranchId);
    }

    [Fact]
    public void An_empty_branch_or_a_dead_session_is_refused()
    {
        var session = Open();
        Assert.Equal(IdentityCodes.SessionBranchInvalid, Assert.Throws<DomainException>(() => session.SelectBranch(Guid.Empty, FixedClock.Instant)).Code);

        session.Revoke(RevocationReason.Logout, FixedClock.Instant);
        Assert.Equal(IdentityCodes.SessionNotLive, Assert.Throws<DomainException>(() => session.SelectBranch(Branch, FixedClock.Instant)).Code);
    }
}

/// <summary>CP-03: recovery mobile and password recovery (intake LIS-INT-FND-001, capability 5).</summary>
public sealed class PlatformUserRecoveryTests
{
    private static PlatformUser NewUser(string? mobile = null) =>
        PlatformUser.Register("lab.admin", "admin@example.com", "Lab Admin", "$argon2id$v=19$m=65536,t=3,p=1$salt$hash", FixedClock.Instant, mobile);

    [Theory]
    [InlineData("+20 100 123 4567", "+201001234567")]
    [InlineData("(010) 0123-4567", "01001234567")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void The_mobile_is_normalised_to_digits_with_an_optional_plus(string? raw, string? expected)
    {
        Assert.Equal(expected, PlatformUser.NormaliseMobile(raw));
        Assert.Equal(expected, NewUser(raw).Mobile);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("abc")]
    [InlineData("+123456789012345678901")]
    public void A_mobile_that_is_too_short_too_long_or_not_digits_is_a_validation_refusal(string raw)
    {
        var refusal = Assert.Throws<DomainException>(() => PlatformUser.NormaliseMobile(raw));

        Assert.Equal(IdentityCodes.MobileInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void Recovering_the_password_replaces_the_hash_rotates_the_stamp_clears_the_lockout_and_raises_one_event()
    {
        var user = NewUser("+201001234567");
        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedPasswordCheck(FixedClock.Instant, 5, TimeSpan.FromMinutes(30));
        }

        var stamp = user.SecurityStamp;
        user.ClearDomainEvents();

        user.RecoverPassword("$argon2id$v=19$m=65536,t=3,p=1$salt$new", FixedClock.Instant.AddMinutes(1));

        Assert.EndsWith("$new", user.PasswordHash, StringComparison.Ordinal);
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.False(user.IsLockedOut);
        Assert.Equal(0, user.FailedPasswordAttempts);
        Assert.Equal(FixedClock.Instant.AddMinutes(1), user.PasswordChangedAtUtc);
        Assert.IsType<PlatformUserPasswordRecovered>(Assert.Single(user.DomainEvents));
    }

    [Fact]
    public void An_inactive_account_cannot_recover_its_password()
    {
        var user = NewUser();
        user.Deactivate(FixedClock.Instant);

        Assert.Equal(IdentityCodes.UserInactive, Assert.Throws<DomainException>(() => user.RecoverPassword("$argon2id$v=19$m=65536,t=3,p=1$salt$new", FixedClock.Instant)).Code);
    }
}
