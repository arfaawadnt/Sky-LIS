using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.CredentialTransport;
using Platform.Contracts.Common;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// 02 §3.4: one challenge per request and the same nonce on every sealed field. A request with two credential fields
/// (current + new password, password + confirmation) opens both; the nonce stays single-use across requests.
/// </summary>
public sealed class CredentialEnvelopeRequestTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Audience = "POST /api/v1/account/password";

    [DatabaseFact]
    public async Task Two_fields_of_one_request_share_the_challenge_and_a_second_request_is_a_replay()
    {
        var challenge = await new AuthClient(factory.CreateClient()).ChallengeAsync();
        var current = TestSealer.Seal(challenge, "currentPassword", "old-secret-value", Audience);
        var next = TestSealer.Seal(challenge, "newPassword", "new-secret-value", Audience);

        using (var scope = factory.Services.CreateScope())
        {
            var opener = scope.ServiceProvider.GetRequiredService<ICredentialEnvelopeOpener>();
            var requestNonces = new HashSet<string>(StringComparer.Ordinal);

            var first = await opener.OpenAsync(current, "currentPassword", Audience, requestNonces, CancellationToken.None);
            var second = await opener.OpenAsync(next, "newPassword", Audience, requestNonces, CancellationToken.None);

            Assert.True(first.Ok, first.Code);
            Assert.True(second.Ok, second.Code);
            Assert.Equal(("old-secret-value", "new-secret-value"), (first.Plaintext, second.Plaintext));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var opener = scope.ServiceProvider.GetRequiredService<ICredentialEnvelopeOpener>();
            var replay = await opener.OpenAsync(next, "newPassword", Audience, new HashSet<string>(StringComparer.Ordinal), CancellationToken.None);

            Assert.False(replay.Ok);
            Assert.Equal(ProblemCodes.CredentialReplayed, replay.Code);
        }
    }
}
