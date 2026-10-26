using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-014 P-SUB (reviewer notes S1–S3): the subscription wizard refuses a manager mobile that already belongs to an account on its
/// field — in the early check, at start and again at complete (where it used to fail as an anonymous <c>UNIQUE-409</c> of
/// <c>ux_platform_users_mobile</c>) — checks the username of a verified request (SUB-013) and names the field of every
/// refusal; changed details start a new request with a new code.
/// </summary>
public sealed class PublicSubscriptionFlowTests : IAsyncLifetime
{
    private const string Subscriptions = "/api/v1/public/subscriptions";
    private const string Password = "a long enough password";

    private static readonly string[] AdminPermissions = ["platform.view", "platform.manage", "tenancy.view", "tenancy.create", "tenancy.edit", "tenancy.manage"];

    private readonly ApiFactory _factory = new();

    public Task InitializeAsync()
    {
        // every test walks the wizard several times from one address: the account policy is not under test here (RateLimitTests)
        _factory.Overrides["Lis:Security:RateLimits:CredentialAccountPer10Minutes"] = "1000";
        return _factory.InitializeAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync();

    [DatabaseFact]
    public async Task The_early_check_marks_a_manager_mobile_an_account_already_holds()
    {
        var place = await CreatePlaceAsync();
        var anonymous = Anonymous();
        var owner = NewMobile();
        await SubscribeAsync(anonymous, place, owner, "owner." + Suffix());

        var free = NewMobile();
        Assert.Empty((await CheckAsync(anonymous, new CheckSubscriptionRequest(place.CountryId, free, NewMobile()))).Issues);

        // the national spelling of the administrator's mobile, as the manager's mobile of a new laboratory
        var manager = await CheckAsync(anonymous, new CheckSubscriptionRequest(place.CountryId, free, owner));
        Assert.Equal(new[] { new SubscriptionFieldIssue("managerMobile", "SUB-011") }, manager.Issues);

        // the international spelling, without a country
        var international = await CheckAsync(anonymous, new CheckSubscriptionRequest(null, null, Global(owner)));
        Assert.Equal(new[] { new SubscriptionFieldIssue("managerMobile", "SUB-011") }, international.Issues);

        // the laboratory's own mobile belongs to no user and is not unique: no rule refuses it
        Assert.Empty((await CheckAsync(anonymous, new CheckSubscriptionRequest(place.CountryId, owner, null))).Issues);

        // the country's rule first: a wrong length is GEO-010 / GEO-014 on its own field
        var lengths = await CheckAsync(anonymous, new CheckSubscriptionRequest(place.CountryId, "012345", "0123"));
        Assert.Equal(new[] { new SubscriptionFieldIssue("mobile", "GEO-010"), new SubscriptionFieldIssue("managerMobile", "GEO-014") }, lengths.Issues);

        var oversized = await anonymous.PostAsJsonAsync($"{Subscriptions}/check", new CheckSubscriptionRequest(null, new string('1', 80), null));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    [DatabaseFact]
    public async Task Start_refuses_a_mobile_an_account_holds_with_SUB_011_naming_the_field_before_any_code_is_sent()
    {
        var place = await CreatePlaceAsync();
        var anonymous = Anonymous();
        var owner = NewMobile();
        await SubscribeAsync(anonymous, place, owner, "owner." + Suffix());
        var sentBefore = _factory.Notifications.Messages.Count;

        var refused = await anonymous.PostAsJsonAsync($"{Subscriptions}/start", Start(place, owner, mobile: Global(owner)));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await ProblemAsync(refused);
        Assert.Equal("SUB-011", problem.Code);
        Assert.Equal("managerMobile", Assert.Single(problem.Fields));
        Assert.All(problem.Rules, rule => Assert.Equal("SUB-011", rule));
        Assert.Equal(sentBefore, _factory.Notifications.Messages.Count);   // no code went out

        // the other refusals of start name their field too
        var wrongLength = await anonymous.PostAsJsonAsync($"{Subscriptions}/start", Start(place, "012345"));
        var length = await ProblemAsync(wrongLength);
        Assert.Equal(("GEO-014", "managerMobile"), (length.Code, Assert.Single(length.Fields)));
    }

    [DatabaseFact]
    public async Task Complete_names_a_mobile_taken_since_the_start_and_then_a_taken_username()
    {
        var place = await CreatePlaceAsync();
        var anonymous = Anonymous();
        var shared = NewMobile();

        // the reviewer's case: two requests for one manager mobile, both verified; the first to finish takes the mobile
        var first = await StartAndVerifyAsync(anonymous, place, shared);
        var second = await StartAndVerifyAsync(anonymous, place, shared);
        var firstUsername = "first." + Suffix();
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(anonymous, first, firstUsername)).StatusCode);

        var blocked = await CompleteAsync(anonymous, second, "second." + Suffix());
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var mobile = await ProblemAsync(blocked);
        Assert.Equal("SUB-011", mobile.Code);   // was UNIQUE-409 (ux_platform_users_mobile) with no field
        Assert.Equal("managerMobile", Assert.Single(mobile.Fields));

        // a free mobile, the first laboratory's username: SUB-013 on the username field
        var third = await StartAndVerifyAsync(anonymous, place, NewMobile());
        var username = await CompleteAsync(anonymous, third, firstUsername.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Conflict, username.StatusCode);
        var taken = await ProblemAsync(username);
        Assert.Equal(("SUB-013", "username"), (taken.Code, Assert.Single(taken.Fields)));

        // the password policy names the password
        var policy = await ProblemAsync(await CompleteAsync(anonymous, third, "third." + Suffix(), "short"));
        Assert.Equal(("CLI-040", "password"), (policy.Code, Assert.Single(policy.Fields)));

        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(anonymous, third, "third." + Suffix())).StatusCode);
    }

    [DatabaseFact]
    public async Task The_username_check_of_step_3_answers_for_a_verified_request_only()
    {
        var place = await CreatePlaceAsync();
        var anonymous = Anonymous();
        var existing = "taken." + Suffix();
        await SubscribeAsync(anonymous, place, NewMobile(), existing);

        var pending = await StartAsync(anonymous, place, NewMobile());
        var early = await anonymous.GetAsync($"{Subscriptions}/{pending.SubscriptionId}/username-available?username=someone");
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("SUB-003", await AuthClient.CodeOf(early));   // only someone who proved a mobile may probe usernames

        var verified = await StartAndVerifyAsync(anonymous, place, NewMobile());
        Assert.Equal(new[] { new SubscriptionFieldIssue("username", "SUB-013") }, (await UsernameAsync(anonymous, verified, existing.ToUpperInvariant())).Issues);
        Assert.Equal(new[] { new SubscriptionFieldIssue("username", "CLI-042") }, (await UsernameAsync(anonymous, verified, "ab")).Issues);
        Assert.Empty((await UsernameAsync(anonymous, verified, "free." + Suffix())).Issues);

        var unknown = await anonymous.GetAsync($"{Subscriptions}/{Guid.CreateVersion7()}/username-available?username=someone");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [DatabaseFact]
    public async Task Changed_details_start_a_new_request_with_a_new_code_and_a_wrong_code_names_the_code_field()
    {
        var place = await CreatePlaceAsync();
        var anonymous = Anonymous();
        var before = NewMobile();
        var verified = await StartAndVerifyAsync(anonymous, place, before);

        // back to step 1, the manager mobile changed: a new request, the code goes to the new number
        var after = NewMobile();
        var restarted = await StartAsync(anonymous, place, after);
        Assert.NotEqual(verified, restarted.SubscriptionId);
        var code = _factory.Notifications.LastTo(Global(after))!.Placeholders["code"];

        var wrong = await anonymous.PostAsJsonAsync($"{Subscriptions}/verify", new VerifySubscriptionRequest(restarted.SubscriptionId, code == "000000" ? "111111" : "000000"));
        var rejected = await ProblemAsync(wrong);
        Assert.Equal(("SUB-010", "code"), (rejected.Code, Assert.Single(rejected.Fields)));

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync($"{Subscriptions}/verify", new VerifySubscriptionRequest(restarted.SubscriptionId, code))).StatusCode);
        var completed = await CompleteAsync(anonymous, restarted.SubscriptionId, "after." + Suffix());
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        // the abandoned request holds nothing: its mobile is free for another laboratory
        Assert.Empty((await CheckAsync(anonymous, new CheckSubscriptionRequest(place.CountryId, null, before))).Issues);
    }

    private HttpClient Anonymous()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private async Task SubscribeAsync(HttpClient anonymous, Place place, string managerMobile, string username)
    {
        var id = await StartAndVerifyAsync(anonymous, place, managerMobile);
        var completed = await CompleteAsync(anonymous, id, username);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    private async Task<SubscriptionStarted> StartAsync(HttpClient anonymous, Place place, string managerMobile)
    {
        var response = await anonymous.PostAsJsonAsync($"{Subscriptions}/start", Start(place, managerMobile));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionStarted>())!;
    }

    private async Task<Guid> StartAndVerifyAsync(HttpClient anonymous, Place place, string managerMobile)
    {
        var started = await StartAsync(anonymous, place, managerMobile);
        var code = _factory.Notifications.LastTo(Global(managerMobile))!.Placeholders["code"];
        var verified = await anonymous.PostAsJsonAsync($"{Subscriptions}/verify", new VerifySubscriptionRequest(started.SubscriptionId, code));
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        return started.SubscriptionId;
    }

    private static async Task<SubscriptionCheckResult> CheckAsync(HttpClient anonymous, CheckSubscriptionRequest request)
    {
        var response = await anonymous.PostAsJsonAsync($"{Subscriptions}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionCheckResult>())!;
    }

    private static async Task<SubscriptionCheckResult> UsernameAsync(HttpClient anonymous, Guid subscriptionId, string username)
    {
        var response = await anonymous.GetAsync($"{Subscriptions}/{subscriptionId}/username-available?username={Uri.EscapeDataString(username)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionCheckResult>())!;
    }

    private async Task<HttpResponseMessage> CompleteAsync(HttpClient anonymous, Guid subscriptionId, string username, string password = Password)
    {
        var path = $"{Subscriptions}/complete";
        var challenge = await new AuthClient(anonymous).ChallengeAsync();
        return await anonymous.PostAsJsonAsync(path, new { subscriptionId, username, password = TestSealer.Seal(challenge, "password", password, "POST " + path) });
    }

    /// <summary>The code, the fields named in <c>errors</c> and the rule codes of <c>rules</c> of a problem answer.</summary>
    private static async Task<(string Code, IReadOnlyList<string> Fields, IReadOnlyList<string> Rules)> ProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var fields = root.TryGetProperty("errors", out var errors) ? errors.EnumerateObject().Select(field => field.Name).ToList() : [];
        var rules = root.TryGetProperty("rules", out var ruleSet)
            ? ruleSet.EnumerateObject().SelectMany(field => field.Value.EnumerateArray()).Select(rule => rule.GetProperty("rule").GetString()!).ToList()
            : [];
        return (root.GetProperty("code").GetString()!, fields, rules);
    }

    private static StartSubscriptionRequest Start(Place place, string managerMobile, string mobile = "01012345678") =>
        new("Al Shifa Lab", "معمل الشفاء", place.CountryId, place.GovernorateId, "5 Nile St.", mobile, "LIC-9", "Dr. Ahmed", managerMobile, "owner@shifa.example", "en");

    /// <summary>A fresh Egyptian mobile (012 + 8 digits): mobiles are unique among platform users.</summary>
    private static string NewMobile() => "012" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    private static string Global(string localMobile) => "+20" + localMobile[1..];

    private static string Suffix() => Guid.CreateVersion7().ToString("N")[^8..];

    private async Task<Place> CreatePlaceAsync()
    {
        var (adminUsername, _, _) = await _factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: AdminPermissions, displayName: "Operations Admin");
        var admin = _factory.CreateClient();
        var (body, _, _) = await new AuthClient(admin).SignInExpectingSuccessAsync(adminUsername, ApiFactory.Password);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        admin.DefaultRequestHeaders.Add("X-Requested-With", "Platform");

        var iso = TestCountryCodes.Next();
        var country = await admin.PostAsJsonAsync("/api/v1/platform/countries", new CountryRequest("Testland " + iso, "أرض الاختبار", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        Assert.Equal(HttpStatusCode.Created, country.StatusCode);
        var countryId = (await country.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var governorateId = (await (await admin.PostAsJsonAsync("/api/v1/platform/governorates", new GovernorateRequest(countryId, "Cairo", "القاهرة", null, null))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var planCode = "T" + Suffix().ToUpperInvariant();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/platform/plans", new PlanRequest(planCode, "Test plan", "باقة", 10m, "EGP", 1, 5, 100, 1))).StatusCode);
        return new Place(countryId, governorateId);
    }

    private sealed record Place(Guid CountryId, Guid GovernorateId);
}
