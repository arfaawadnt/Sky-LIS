using System.Net;
using Platform.Domain.Identity;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Identity;

public sealed class RefreshSessionTests
{
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly TimeSpan Sliding = TimeSpan.FromHours(8);
    private static readonly TimeSpan Absolute = TimeSpan.FromHours(12);

    private static byte[] Hash(byte seed) => Enumerable.Repeat(seed, RefreshSession.TokenHashLength).ToArray();

    private static RefreshSession OpenBrowserSession() =>
        RefreshSession.Open(User, Tenant, SessionKind.Browser, SessionScope.Full, Hash(1), FixedClock.Instant, Sliding, Absolute, IPAddress.Loopback, "Mozilla/5.0");

    [Fact]
    public void Opening_a_session_starts_a_family_with_sliding_and_absolute_expiry()
    {
        var session = OpenBrowserSession();

        Assert.Equal(FixedClock.Instant + Sliding, session.ExpiresAtUtc);
        Assert.Equal(FixedClock.Instant + Absolute, session.AbsoluteExpiresAtUtc);
        Assert.NotEqual(Guid.Empty, session.FamilyId);
        Assert.True(session.IsLiveAt(FixedClock.Instant.AddHours(7)));
        Assert.False(session.IsLiveAt(FixedClock.Instant.AddHours(9)));
        Assert.Equal(IPAddress.Loopback, session.IpAddress);
        Assert.IsType<RefreshSessionOpened>(Assert.Single(session.DomainEvents));
    }

    [Fact]
    public void A_session_is_idle_once_nothing_renewed_it_for_longer_than_the_timeout_and_a_rotation_starts_the_count_again_F_01()
    {
        var session = OpenBrowserSession();
        var timeout = TimeSpan.FromMinutes(30);

        Assert.False(session.IsIdleAt(FixedClock.Instant.AddMinutes(30), timeout));
        Assert.True(session.IsIdleAt(FixedClock.Instant.AddMinutes(30).AddSeconds(1), timeout));
        Assert.False(session.IsIdleAt(FixedClock.Instant.AddHours(1), TimeSpan.Zero));   // no timeout, no idle end

        var successor = session.Rotate(Hash(2), FixedClock.Instant.AddMinutes(20), Sliding);
        Assert.False(successor.IsIdleAt(FixedClock.Instant.AddMinutes(45), timeout));
        Assert.True(successor.IsIdleAt(FixedClock.Instant.AddMinutes(51), timeout));
    }

    [Fact]
    public void A_hash_of_the_wrong_length_or_an_absolute_lifetime_below_the_sliding_one_is_refused()
    {
        Assert.Equal(IdentityCodes.SessionTokenHashInvalid, Assert.Throws<DomainException>(() => RefreshSession.Open(User, Tenant, SessionKind.Browser, SessionScope.Full, [1, 2, 3], FixedClock.Instant, Sliding, Absolute)).Code);
        Assert.Equal(IdentityCodes.SessionLifetimeInvalid, Assert.Throws<DomainException>(() => RefreshSession.Open(User, Tenant, SessionKind.Browser, SessionScope.Full, Hash(1), FixedClock.Instant, Absolute, Sliding)).Code);
    }

    [Fact]
    public void Rotation_kills_the_presented_session_and_issues_a_successor_in_the_same_family()
    {
        var session = OpenBrowserSession();
        var at = FixedClock.Instant.AddHours(6);

        var successor = session.Rotate(Hash(2), at, Sliding);

        Assert.Equal(session.FamilyId, successor.FamilyId);
        Assert.Equal(successor.Id, session.SuccessorId);
        Assert.Equal(at, session.RotatedAtUtc);
        Assert.False(session.IsLiveAt(at.AddSeconds(1)));
        Assert.True(successor.IsLiveAt(at.AddHours(5)));
        Assert.Equal(session.AbsoluteExpiresAtUtc, successor.AbsoluteExpiresAtUtc);
        Assert.Equal(session.AbsoluteExpiresAtUtc, successor.ExpiresAtUtc); // 6 h + 8 h sliding is capped by the 12 h absolute limit
        Assert.Contains(session.DomainEvents, domainEvent => domainEvent is RefreshSessionRotated);
    }

    [Fact]
    public void Presenting_a_rotated_session_again_is_refused_so_the_caller_can_treat_it_as_reuse()
    {
        var session = OpenBrowserSession();
        session.Rotate(Hash(2), FixedClock.Instant.AddMinutes(1), Sliding);

        var refusal = Assert.Throws<DomainException>(() => session.Rotate(Hash(3), FixedClock.Instant.AddMinutes(2), Sliding));

        Assert.Equal(IdentityCodes.SessionNotLive, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
    }

    [Fact]
    public void Revocation_is_idempotent_and_keeps_the_first_reason()
    {
        var session = OpenBrowserSession();
        session.ClearDomainEvents();

        session.Revoke(RevocationReason.ReuseDetected, FixedClock.Instant);
        session.Revoke(RevocationReason.Logout, FixedClock.Instant.AddMinutes(1));

        Assert.True(session.IsRevoked);
        Assert.Equal(RevocationReason.ReuseDetected, session.RevocationReason);
        Assert.Equal(FixedClock.Instant, session.RevokedAtUtc);
        Assert.Single(session.DomainEvents);
        Assert.False(session.IsLiveAt(FixedClock.Instant));
    }

    [Fact]
    public void An_mfa_pending_session_becomes_full_once_and_a_dead_session_cannot_be_reauthenticated()
    {
        var pending = RefreshSession.Open(User, Tenant, SessionKind.Browser, SessionScope.MfaPending, Hash(5), FixedClock.Instant, Sliding, Absolute);

        pending.GrantFullScope(FixedClock.Instant);

        Assert.Equal(SessionScope.Full, pending.Scope);
        Assert.Equal(IdentityCodes.SessionScopeTransitionInvalid, Assert.Throws<DomainException>(() => pending.GrantFullScope(FixedClock.Instant)).Code);

        pending.MarkReauthenticated(FixedClock.Instant.AddMinutes(1));
        Assert.Equal(FixedClock.Instant.AddMinutes(1), pending.ReauthenticatedAtUtc);

        pending.Revoke(RevocationReason.Logout, FixedClock.Instant.AddMinutes(2));
        Assert.Equal(IdentityCodes.SessionNotLive, Assert.Throws<DomainException>(() => pending.MarkReauthenticated(FixedClock.Instant.AddMinutes(3))).Code);
    }

    [Fact]
    public void A_long_user_agent_is_truncated_and_touch_updates_last_seen()
    {
        var session = RefreshSession.Open(User, null, SessionKind.Machine, SessionScope.Full, Hash(9), FixedClock.Instant, Sliding, Absolute, null, new string('u', 600));

        Assert.Equal(RefreshSession.MaxUserAgentLength, session.UserAgent!.Length);
        Assert.Null(session.TenantId);

        session.Touch(FixedClock.Instant.AddMinutes(5), IPAddress.IPv6Loopback);

        Assert.Equal(FixedClock.Instant.AddMinutes(5), session.LastSeenAtUtc);
        Assert.Equal(IPAddress.IPv6Loopback, session.IpAddress);
    }
}
