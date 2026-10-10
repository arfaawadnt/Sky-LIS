using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Contracts.Integration;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Machine credentials over the real API (02 §4.2–§4.3, §6.3; intake LIS-INT-INT-001 M1–M5): the exchange with a sealed
/// secret, refresh rotation and reuse detection, revocation, refusals that do not say which part was wrong, and scopes.
/// </summary>
public sealed class MachineCredentialFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal const string TokenPath = "/api/v1/auth/token";
    internal const string RefreshPath = "/api/v1/auth/token/refresh";
    internal const string CredentialsPath = "/api/v1/identity/machine-credentials";

    private static readonly string[] AdminPermissions = ["integration.view", "integration.create", "integration.manage"];

    [DatabaseFact]
    public async Task A_credential_exchanges_for_tokens_refresh_rotates_and_reuse_revokes_the_family()
    {
        var admin = await AdminClientAsync(Guid.CreateVersion7());
        var credential = await IssueAsync(admin, "Exchange robot", ["integration.view"]);
        var client = factory.CreateClient();

        var exchanged = await ExchangeAsync(client, credential.ClientId, credential.ClientSecret);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        var tokens = (await exchanged.Content.ReadFromJsonAsync<MachineTokenResponse>())!;
        Assert.Equal(("Bearer", true), (tokens.TokenType, tokens.ExpiresAtUtc > factory.Clock.UtcNow));
        Assert.Equal(["integration.view"], tokens.Scopes);
        Assert.False(exchanged.Headers.TryGetValues("Set-Cookie", out _));   // machines never get the cookie

        var me = await new AuthClient(client).MeAsync(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("machine", (await me.Content.ReadFromJsonAsync<SessionResponse>())!.Kind);

        var refreshed = await client.PostAsJsonAsync(RefreshPath, new MachineTokenRefreshRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var rotated = (await refreshed.Content.ReadFromJsonAsync<MachineTokenResponse>())!;
        Assert.NotEqual(tokens.RefreshToken, rotated.RefreshToken);

        var reused = await client.PostAsJsonAsync(RefreshPath, new MachineTokenRefreshRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(ProblemCodes.RefreshTokenRejected, await AuthClient.CodeOf(reused));

        var successorAfterReuse = await client.PostAsJsonAsync(RefreshPath, new MachineTokenRefreshRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, successorAfterReuse.StatusCode);   // the whole family was revoked
    }

    [DatabaseFact]
    public async Task A_wrong_secret_and_an_unknown_client_are_refused_alike_and_plaintext_is_refused()
    {
        var admin = await AdminClientAsync(Guid.CreateVersion7());
        var credential = await IssueAsync(admin, "Refusal robot", ["integration.view"]);
        var client = factory.CreateClient();

        var wrongSecret = await ExchangeAsync(client, credential.ClientId, credential.ClientSecret[..^2] + "AA");
        var unknownClient = await ExchangeAsync(client, "lis-00000000000000000000000000000000", credential.ClientSecret);
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (wrongSecret.StatusCode, unknownClient.StatusCode));
        Assert.Equal((ProblemCodes.InvalidCredentials, ProblemCodes.InvalidCredentials), (await AuthClient.CodeOf(wrongSecret), await AuthClient.CodeOf(unknownClient)));
        Assert.Equal(await TitleOf(wrongSecret), await TitleOf(unknownClient));

        var plaintext = await client.PostAsJsonAsync(TokenPath, new { clientId = credential.ClientId, clientSecret = credential.ClientSecret });
        Assert.Equal(HttpStatusCode.BadRequest, plaintext.StatusCode);
        Assert.Equal(ProblemCodes.CredentialPlaintext, await AuthClient.CodeOf(plaintext));
    }

    [DatabaseFact]
    public async Task A_revoked_credential_is_refused_at_exchange_refresh_and_on_its_live_token()
    {
        var admin = await AdminClientAsync(Guid.CreateVersion7());
        var credential = await IssueAsync(admin, "Revoked robot", ["integration.view"]);
        var client = factory.CreateClient();
        var tokens = (await (await ExchangeAsync(client, credential.ClientId, credential.ClientSecret)).Content.ReadFromJsonAsync<MachineTokenResponse>())!;

        var revoked = await admin.PostAsJsonAsync($"{CredentialsPath}/{credential.Id}/revoke", new RevokeMachineCredentialRequest("rotated out"));
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var exchange = await ExchangeAsync(client, credential.ClientId, credential.ClientSecret);
        Assert.Equal(ProblemCodes.InvalidCredentials, await AuthClient.CodeOf(exchange));
        var refresh = await client.PostAsJsonAsync(RefreshPath, new MachineTokenRefreshRequest(tokens.RefreshToken));
        Assert.Equal(ProblemCodes.RefreshTokenRejected, await AuthClient.CodeOf(refresh));
        Assert.Equal(HttpStatusCode.Unauthorized, (await new AuthClient(client).MeAsync(tokens.AccessToken)).StatusCode);

        var list = await admin.GetFromJsonAsync<Paged<MachineCredentialListItem>>(CredentialsPath + "?includeRevoked=true");
        Assert.Equal("Revoked", list!.Items.Single(item => item.Id == credential.Id).Status);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<Paged<MachineCredentialListItem>>(CredentialsPath))!.Items, item => item.Id == credential.Id);
    }

    [DatabaseFact]
    public async Task Rotation_kills_the_old_secret_and_scopes_are_enforced_on_the_agent_api()
    {
        var admin = await AdminClientAsync(Guid.CreateVersion7());
        var reader = await IssueAsync(admin, "Reader robot", ["integration.view"]);
        var writer = await IssueAsync(admin, "Writer robot", ["integration.create", "integration.view"]);

        var rotated = (await (await admin.PostAsync($"{CredentialsPath}/{reader.Id}/rotate", null)).Content.ReadFromJsonAsync<MachineCredentialSecretResponse>())!;
        Assert.Equal(reader.ClientId, rotated.ClientId);
        Assert.Equal(ProblemCodes.InvalidCredentials, await AuthClient.CodeOf(await ExchangeAsync(factory.CreateClient(), reader.ClientId, reader.ClientSecret)));

        var readerClient = await MachineClientAsync(factory, rotated.ClientId, rotated.ClientSecret);
        var forbidden = await readerClient.PostAsJsonAsync("/api/v1/agents/heartbeat", new AgentHeartbeatRequest("1.0", null, null, []));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(forbidden));

        var writerClient = await MachineClientAsync(factory, writer.ClientId, writer.ClientSecret);
        var notAnAgent = await writerClient.PostAsJsonAsync("/api/v1/agents/heartbeat", new AgentHeartbeatRequest("1.0", null, null, []));
        Assert.Equal(HttpStatusCode.Forbidden, notAnAgent.StatusCode);
        Assert.Equal("INT-035", await AuthClient.CodeOf(notAnAgent));

        var adminOnly = await writerClient.PostAsJsonAsync(CredentialsPath, new CreateMachineCredentialRequest("Escalation", ["integration.manage"], null));
        Assert.Equal(HttpStatusCode.Forbidden, adminOnly.StatusCode);
    }

    [DatabaseFact]
    public async Task Issuing_is_refused_beyond_the_actors_privileges_and_on_bad_shapes()
    {
        var admin = await AdminClientAsync(Guid.CreateVersion7());

        var beyond = await admin.PostAsJsonAsync(CredentialsPath, new CreateMachineCredentialRequest("Escalation", ["identity.manage"], null));
        Assert.Equal(HttpStatusCode.Forbidden, beyond.StatusCode);
        Assert.Equal("MCR-004", await AuthClient.CodeOf(beyond));

        var unknown = await admin.PostAsJsonAsync(CredentialsPath, new CreateMachineCredentialRequest("Unknown", ["integration.fly"], null));
        Assert.Equal("MCR-002", await AuthClient.CodeOf(unknown));

        var empty = await admin.PostAsJsonAsync(CredentialsPath, new CreateMachineCredentialRequest("", [], null));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(empty));

        await IssueAsync(admin, "Twin robot", ["integration.view"]);
        var twin = await admin.PostAsJsonAsync(CredentialsPath, new CreateMachineCredentialRequest("twin robot", ["integration.view"], null));
        Assert.Equal(HttpStatusCode.Conflict, twin.StatusCode);
        Assert.Equal("MCR-100", await AuthClient.CodeOf(twin));
    }

    internal static async Task<HttpResponseMessage> ExchangeAsync(HttpClient client, string clientId, string clientSecret)
    {
        var challenge = await new AuthClient(client).ChallengeAsync();
        var sealedSecret = TestSealer.Seal(challenge, "clientSecret", clientSecret, "POST " + TokenPath);
        return await client.PostAsJsonAsync(TokenPath, new { clientId, clientSecret = sealedSecret });
    }

    /// <summary>A client carrying a machine access token (no CSRF header: machines use bearer tokens only).</summary>
    internal static async Task<HttpClient> MachineClientAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string clientId, string clientSecret)
    {
        var client = host.CreateClient();
        var response = await ExchangeAsync(client, clientId, clientSecret);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = (await response.Content.ReadFromJsonAsync<MachineTokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<string?> TitleOf(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString();
    }

    private async Task<HttpClient> AdminClientAsync(Guid tenant)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: AdminPermissions, displayName: "Integration Admin");
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private static async Task<MachineCredentialSecretResponse> IssueAsync(HttpClient admin, string name, string[] scopes)
    {
        var response = await admin.PostAsJsonAsync(CredentialsPath, new CreateMachineCredentialRequest(name, scopes, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MachineCredentialSecretResponse>())!;
    }
}
