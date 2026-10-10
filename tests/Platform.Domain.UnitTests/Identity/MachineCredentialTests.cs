using Platform.Domain.Identity;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Identity;

/// <summary>Scenarios M1–M3 of intake LIS-INT-INT-001 on the MachineCredential aggregate (02 §6.3).</summary>
public sealed class MachineCredentialTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private const string ClientId = "lis-0123456789abcdef0123456789abcdef";

    private static MachineCredential Issue(IEnumerable<string>? scopes = null, DateTimeOffset? expires = null) =>
        MachineCredential.Issue(Guid.CreateVersion7(), ClientId, " Site agent ", "v1.hash", Guid.CreateVersion7(), scopes ?? ["integration.view", "integration.create", "integration.view"], Guid.CreateVersion7(), expires, Now);

    [Fact]
    public void M1_Issue_happy_path_defaults_to_one_year_and_sorts_scopes()
    {
        var credential = Issue();

        Assert.Equal(("Site agent", ClientId), (credential.Name, credential.ClientId));
        Assert.Equal(["integration.create", "integration.view"], credential.Scopes);
        Assert.Equal(Now + MachineCredential.DefaultLifetime, credential.ExpiresAtUtc);
        Assert.True(credential.IsLiveAt(Now));
        var raised = Assert.IsType<MachineCredentialIssued>(Assert.Single(credential.DomainEvents));
        Assert.Equal((credential.Id, ClientId), (raised.CredentialId, raised.ClientId));
    }

    [Fact]
    public void M1_Scopes_must_be_catalogued_MCR_002()
    {
        Assert.Equal(MachineCredentialCodes.ScopesInvalid, Assert.Throws<DomainException>(() => Issue(["integration.fly"])).Code);
        Assert.Equal(MachineCredentialCodes.ScopesInvalid, Assert.Throws<DomainException>(() => Issue([])).Code);
    }

    [Fact]
    public void M1_Expiry_is_future_and_at_most_two_years_MCR_003()
    {
        Assert.Equal(MachineCredentialCodes.ExpiryInvalid, Assert.Throws<DomainException>(() => Issue(expires: Now.AddMinutes(-1))).Code);
        Assert.Equal(MachineCredentialCodes.ExpiryInvalid, Assert.Throws<DomainException>(() => Issue(expires: Now.AddDays(731))).Code);
    }

    [Theory]
    [InlineData("lis-0123")]
    [InlineData("LIS-0123456789abcdef0123456789abcdef")]
    [InlineData("lis-0123456789abcdef0123456789abcdeG")]
    public void M1_Client_id_shape_is_checked_MCR_005(string clientId)
    {
        Assert.False(MachineCredential.IsValidClientId(clientId));
        Assert.Equal(MachineCredentialCodes.ClientIdInvalid, Assert.Throws<DomainException>(() => MachineCredential.Issue(Guid.CreateVersion7(), clientId, "x", "v1.h", Guid.CreateVersion7(), ["integration.view"], null, null, Now)).Code);
    }

    [Fact]
    public void M2_Rotate_replaces_the_hash_of_a_live_credential_only_MCR_010()
    {
        var credential = Issue();
        credential.ClearDomainEvents();

        credential.RotateSecret("v1.new", Now);
        Assert.Equal(("v1.new", (DateTimeOffset?)Now), (credential.SecretHash, credential.SecretRotatedAtUtc));
        Assert.IsType<MachineCredentialSecretRotated>(Assert.Single(credential.DomainEvents));

        credential.Revoke(Guid.CreateVersion7(), null, Now);
        Assert.Equal(MachineCredentialCodes.CredentialNotLive, Assert.Throws<DomainException>(() => credential.RotateSecret("v1.again", Now)).Code);
    }

    [Fact]
    public void M3_Revoke_is_idempotent_and_keeps_the_first_record()
    {
        var credential = Issue();
        var first = Guid.CreateVersion7();
        credential.ClearDomainEvents();

        credential.Revoke(first, " leaked ", Now);
        credential.Revoke(Guid.CreateVersion7(), "again", Now.AddMinutes(1));

        Assert.Equal((first, "leaked", (DateTimeOffset?)Now), (credential.RevokedByUserId, credential.RevocationReason, credential.RevokedAtUtc));
        Assert.False(credential.IsLiveAt(Now));
        Assert.IsType<MachineCredentialRevoked>(Assert.Single(credential.DomainEvents));
    }

    [Fact]
    public void An_expired_credential_is_not_live()
    {
        var credential = Issue(expires: Now.AddDays(1));
        Assert.False(credential.IsLiveAt(Now.AddDays(1)));
        Assert.Equal("svc." + ClientId, MachineCredential.ServiceUsername(ClientId));
    }
}
