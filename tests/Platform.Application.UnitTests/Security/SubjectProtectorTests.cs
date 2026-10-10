using Platform.Application.UnitTests.Support;
using Platform.Infrastructure.Security.Tokens;

namespace Platform.Application.UnitTests.Security;

/// <summary>SubjectProtectorTests (01 §8, D-02): round trip, unlinkability, tamper, unknown kid, previous key.</summary>
public sealed class SubjectProtectorTests
{
    private const string SigningKid = "2026-10#1";
    private static readonly Guid User = Guid.CreateVersion7();

    [Fact]
    public void Round_trips_and_differs_per_call()
    {
        var protector = new SubjectProtector(TestKeys.ValidOptions());

        var first = protector.Protect(User, SigningKid);
        var second = protector.Protect(User, SigningKid);

        Assert.StartsWith($"v1.{protector.ActiveKeyId}.", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal(User, protector.Unprotect(first, SigningKid));
        Assert.Equal(User, protector.Unprotect(second, SigningKid));
    }

    [Fact]
    public void A_tampered_plaintext_or_unknown_subject_is_refused()
    {
        var protector = new SubjectProtector(TestKeys.ValidOptions());
        var subject = protector.Protect(User, SigningKid);
        var parts = subject.Split('.');
        var payload = parts[2].ToCharArray();
        payload[^1] = payload[^1] == 'A' ? 'B' : 'A';

        Assert.Null(protector.Unprotect($"{parts[0]}.{parts[1]}.{new string(payload)}", SigningKid));
        Assert.Null(protector.Unprotect(User.ToString(), SigningKid));
        Assert.Null(protector.Unprotect($"v1.deadbeef.{parts[2]}", SigningKid));
        Assert.Null(protector.Unprotect(string.Empty, SigningKid));
    }

    [Fact]
    public void The_subject_is_bound_to_the_signing_key_epoch()
    {
        var protector = new SubjectProtector(TestKeys.ValidOptions());
        var subject = protector.Protect(User, SigningKid);

        Assert.Null(protector.Unprotect(subject, "2026-11#2"));
    }

    [Fact]
    public void A_previous_key_still_decrypts_but_never_encrypts()
    {
        var oldOptions = TestKeys.ValidOptions();
        var oldProtector = new SubjectProtector(oldOptions);
        var oldSubject = oldProtector.Protect(User, SigningKid);

        var rotated = new SubjectProtector(TestKeys.ValidOptions(previousSubjectKey: oldOptions.SubjectEncryptionKey));

        Assert.Equal(User, rotated.Unprotect(oldSubject, SigningKid));
        Assert.NotEqual(oldProtector.ActiveKeyId, rotated.ActiveKeyId);
        Assert.StartsWith($"v1.{rotated.ActiveKeyId}.", rotated.Protect(User, SigningKid), StringComparison.Ordinal);
        Assert.Null(oldProtector.Unprotect(rotated.Protect(User, SigningKid), SigningKid));
    }
}
