using Platform.Domain.Identity;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Identity;

public sealed class PlatformUserTests
{
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(30);

    private static PlatformUser NewUser() => PlatformUser.Register("  Lab.Admin ", "Admin@Example.COM", " Lab Admin ", "$argon2id$v=19$m=65536,t=3,p=1$salt$hash", FixedClock.Instant);

    [Fact]
    public void Registration_normalises_identity_fields_and_raises_one_event()
    {
        var user = NewUser();

        Assert.Equal("lab.admin", user.Username);
        Assert.Equal("admin@example.com", user.Email);
        Assert.Equal("Lab Admin", user.DisplayName);
        Assert.True(user.IsActive);
        Assert.NotEqual(Guid.Empty, user.SecurityStamp);
        Assert.Equal(FixedClock.Instant, user.PasswordChangedAtUtc);
        var registered = Assert.IsType<PlatformUserRegistered>(Assert.Single(user.DomainEvents));
        Assert.Equal(user.Id, registered.UserId);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("bad#char")]
    public void An_invalid_username_is_a_validation_refusal(string username)
    {
        var refusal = Assert.Throws<DomainException>(() => PlatformUser.Register(username, "a@b.c", "Name", "hash", FixedClock.Instant));

        Assert.Equal(IdentityCodes.UsernameInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("@x")]
    [InlineData("a@b@c")]
    public void An_invalid_email_is_a_validation_refusal(string email)
    {
        var refusal = Assert.Throws<DomainException>(() => PlatformUser.Register("user.one", email, "Name", "hash", FixedClock.Instant));

        Assert.Equal(IdentityCodes.EmailInvalid, refusal.Code);
    }

    [Fact]
    public void Five_checked_failures_lock_the_account_and_a_success_after_the_lockout_clears_it()
    {
        var user = NewUser();
        user.ClearDomainEvents();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            user.RecordFailedPasswordCheck(FixedClock.Instant, lockoutThreshold: 5, Lockout);
        }

        Assert.False(user.IsLockedOut);
        user.RecordFailedPasswordCheck(FixedClock.Instant, lockoutThreshold: 5, Lockout);

        Assert.True(user.IsLockedOutAt(FixedClock.Instant.AddMinutes(29)));
        Assert.False(user.IsLockedOutAt(FixedClock.Instant.AddMinutes(31)));
        var locked = Assert.IsType<PlatformUserLockedOut>(Assert.Single(user.DomainEvents));
        Assert.Equal(FixedClock.Instant + Lockout, locked.LockoutEndUtc);

        var refusal = Assert.Throws<DomainException>(() => user.RecordSuccessfulPasswordCheck(FixedClock.Instant.AddMinutes(1)));
        Assert.Equal(IdentityCodes.UserLockedOut, refusal.Code);

        user.RecordSuccessfulPasswordCheck(FixedClock.Instant.AddMinutes(31));
        Assert.False(user.IsLockedOut);
        Assert.Equal(0, user.FailedPasswordAttempts);
    }

    [Fact]
    public void Changing_the_password_rotates_the_security_stamp_and_resets_the_lockout()
    {
        var user = NewUser();
        var stampBefore = user.SecurityStamp;
        user.RecordFailedPasswordCheck(FixedClock.Instant, 1, Lockout);

        user.ChangePassword("new-hash", FixedClock.Instant.AddDays(1));

        Assert.NotEqual(stampBefore, user.SecurityStamp);
        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Equal(FixedClock.Instant.AddDays(1), user.PasswordChangedAtUtc);
        Assert.False(user.IsLockedOut);
        Assert.Contains(user.DomainEvents, domainEvent => domainEvent is PlatformUserPasswordChanged);
    }

    [Fact]
    public void A_deactivated_user_cannot_sign_in_or_change_the_password()
    {
        var user = NewUser();

        user.Deactivate(FixedClock.Instant);

        Assert.False(user.IsActive);
        Assert.Equal(IdentityCodes.UserInactive, Assert.Throws<DomainException>(() => user.RecordSuccessfulPasswordCheck(FixedClock.Instant)).Code);
        Assert.Equal(IdentityCodes.UserInactive, Assert.Throws<DomainException>(() => user.ChangePassword("x", FixedClock.Instant)).Code);
        Assert.Contains(user.DomainEvents, domainEvent => domainEvent is PlatformUserDeactivated);
    }
}
