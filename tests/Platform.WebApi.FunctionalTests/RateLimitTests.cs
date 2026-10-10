using System.Net;
using Platform.Contracts.Common;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>SEC-E08 (02 §8, pitfall P-40): the 429 shape and headers, and a throttled request never counts toward a lockout.</summary>
public sealed class RateLimitTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    public Task InitializeAsync()
    {
        _factory.Overrides["Lis:Security:RateLimits:CredentialChallengePerMinute"] = "3";
        _factory.Overrides["Lis:Security:RateLimits:CredentialAccountPer10Minutes"] = "2";
        return _factory.InitializeAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync();

    [DatabaseFact]
    public async Task The_challenge_limit_rejects_with_retry_after_and_the_policy_name()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AuthClient.ChallengePath)).StatusCode);
        }

        var rejected = await client.GetAsync(AuthClient.ChallengePath);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(ProblemCodes.RateLimited, await AuthClient.CodeOf(rejected));
        Assert.True(rejected.Headers.RetryAfter is not null);
        Assert.Equal("credential-challenge", rejected.Headers.GetValues("RateLimit-Policy").Single());
        Assert.Equal("nosniff", rejected.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [DatabaseFact]
    public async Task A_throttled_sign_in_does_not_increment_the_lockout_counter()
    {
        var (username, _, userId) = await _factory.SeedUserAsync();
        var auth = new AuthClient(_factory.CreateClient());
        var sealedWrong = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            sealedWrong.Add(TestSealer.Seal(await auth.ChallengeAsync(), "password", "wrong", "POST " + AuthClient.SignInPath));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.SignInRawAsync(username, sealedWrong[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.SignInRawAsync(username, sealedWrong[1])).StatusCode);
        var throttled = await auth.SignInRawAsync(username, sealedWrong[2]);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal("credential-account", throttled.Headers.GetValues("RateLimit-Policy").Single());
        Assert.Equal(2, (await _factory.LoadUserAsync(userId)).FailedPasswordAttempts);

        var otherAccount = await new AuthClient(_factory.CreateClient()).SignInRawAsync(username + "x", sealedWrong[2]);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherAccount.StatusCode);   // the account partition is independent of the address
    }
}
