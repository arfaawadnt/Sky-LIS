using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-005 (separate sign-in addresses): a laboratory's users sign in at <c>/lab/{slug}</c> (body <c>{ laboratory }</c>), the
/// Super Admin's staff at <c>/admin/login</c> (body <c>{ admin: true }</c>); exactly one target, a malformed address is a 400;
/// every other miss answers like a wrong password (<c>AUTH-002</c>), a deactivated laboratory keeps <c>CLI-020</c> for its
/// members. The profile carries the address back (<c>portalSlug</c>); the sign-in page reads the laboratory's public card and logo.
/// </summary>
public sealed class PortalAddressesFlowTests : IClassFixture<ApiFactory>
{
    private const string Card = "/api/v1/public/laboratories";
    private static readonly string FilesRoot = Path.Combine(Path.GetTempPath(), "lis-functional-files");
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    private readonly ApiFactory _factory;

    public PortalAddressesFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Overrides["Lis:Files:Root"] = FilesRoot;
    }

    [DatabaseFact]
    public async Task A_member_signs_in_at_the_laboratory_address_and_nowhere_else()
    {
        var (labId, slug) = await LaboratoryAsync(Kind.Active);
        var (otherId, otherSlug) = await LaboratoryAsync(Kind.Active);
        var (member, _, _) = await _factory.SeedUserAsync(tenantId: labId);
        var (stranger, _, _) = await _factory.SeedUserAsync(tenantId: otherId);
        var (staff, _, _) = await _factory.SeedUserAsync(tenantId: TenantIds.Platform);
        var auth = new AuthClient(_factory.CreateClient());

        // at its own address (case and spaces as typed do not matter): signed in, and the profile names the address
        var (signedIn, cookie, _) = await auth.SignInExpectingSuccessAsync(member, ApiFactory.Password, SignInTarget.At(" " + slug.ToUpperInvariant() + " "));
        Assert.Equal((AuthResponse.StepSignedIn, labId, slug), (signedIn.Step, signedIn.Profile!.TenantId, signedIn.Profile.PortalSlug));
        var refreshed = await auth.RefreshAsync(cookie);
        Assert.Equal(slug, (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!.PortalSlug);

        // another laboratory's address, an unknown address, the Super Admin address: all like a wrong password
        await AssertRefusedAsync(auth, member, SignInTarget.At(otherSlug), ProblemCodes.InvalidCredentials);
        await AssertRefusedAsync(auth, member, SignInTarget.At("no-such-lab-" + Guid.CreateVersion7().ToString("N")[^8..]), ProblemCodes.InvalidCredentials);
        await AssertRefusedAsync(auth, member, SignInTarget.Administration, ProblemCodes.InvalidCredentials);
        await AssertRefusedAsync(auth, stranger, SignInTarget.At(slug), ProblemCodes.InvalidCredentials);
        await AssertRefusedAsync(auth, staff, SignInTarget.At(slug), ProblemCodes.InvalidCredentials);

        // a wrong password at a wrong address is still a wrong password (and counts toward the lockout as before)
        var wrong = await auth.SignInAsync(member, "not-the-password", SignInTarget.At("no-such-lab"));
        Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.InvalidCredentials), (wrong.StatusCode, await AuthClient.CodeOf(wrong)));
        Assert.Equal(1, (await _factory.LoadUserAsync(signedIn.Profile.UserId)).FailedPasswordAttempts);
    }

    [DatabaseFact]
    public async Task Platform_staff_sign_in_at_the_Super_Admin_address_only()
    {
        var (staff, _, _) = await _factory.SeedUserAsync(tenantId: TenantIds.Platform);
        var auth = new AuthClient(_factory.CreateClient());

        var (signedIn, _, _) = await auth.SignInExpectingSuccessAsync(staff, ApiFactory.Password, SignInTarget.Administration);
        Assert.Equal((TenantIds.Platform, (string?)null), (signedIn.Profile!.TenantId, signedIn.Profile.PortalSlug));

        // an unknown account at the Super Admin address: the same refusal
        await AssertRefusedAsync(auth, "nobody." + Guid.CreateVersion7().ToString("N")[^10..], SignInTarget.Administration, ProblemCodes.InvalidCredentials);
    }

    [DatabaseFact]
    public async Task The_request_names_exactly_one_well_formed_target()
    {
        var (labId, slug) = await LaboratoryAsync(Kind.Active);
        var (member, _, _) = await _factory.SeedUserAsync(tenantId: labId);
        var auth = new AuthClient(_factory.CreateClient());

        foreach (var target in new[] { SignInTarget.None, new SignInTarget(null, false), new SignInTarget(slug, true), SignInTarget.At("ab"), SignInTarget.At("al noor"), SignInTarget.At("al_noor"), SignInTarget.At(new string('a', 41)) })
        {
            var refused = await auth.SignInAsync(member, ApiFactory.Password, target);
            Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (refused.StatusCode, await AuthClient.CodeOf(refused)));
        }

        // the old body (tenant id, no target) is refused the same way: the address decides now
        var challenge = await auth.ChallengeAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, AuthClient.SignInPath)
        {
            Content = JsonContent.Create(new { tenant = labId, username = member, password = TestSealer.Seal(challenge, "password", ApiFactory.Password, "POST " + AuthClient.SignInPath) }),
        };
        request.Headers.Add("X-Requested-With", "Platform");
        Assert.Equal(HttpStatusCode.BadRequest, (await auth.Http.SendAsync(request)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_trial_laboratory_signs_in_and_one_that_ended_or_was_deactivated_says_so_to_its_members_only()
    {
        var (trialId, trialSlug) = await LaboratoryAsync(Kind.Trial);
        var (endedId, endedSlug) = await LaboratoryAsync(Kind.TrialEnded);
        var (inactiveId, inactiveSlug) = await LaboratoryAsync(Kind.Inactive);
        var auth = new AuthClient(_factory.CreateClient());

        var (trialMember, _, _) = await _factory.SeedUserAsync(tenantId: trialId);
        Assert.Equal(trialSlug, (await auth.SignInExpectingSuccessAsync(trialMember, ApiFactory.Password)).Body.Profile!.PortalSlug);

        var (endedMember, _, _) = await _factory.SeedUserAsync(tenantId: endedId);
        var (inactiveMember, _, _) = await _factory.SeedUserAsync(tenantId: inactiveId);
        // LIS-CR-012 (D-3): a trial that ended 16 days ago is past its 7 days of grace: a member who is not an administrator is refused SUB-020 (403)
        var expired = await auth.SignInAsync(endedMember, ApiFactory.Password, SignInTarget.At(endedSlug));
        Assert.Equal((HttpStatusCode.Forbidden, SubscriptionCodes.Expired), (expired.StatusCode, await AuthClient.CodeOf(expired)));
        await AssertRefusedAsync(auth, inactiveMember, SignInTarget.At(inactiveSlug), PlatformAdminCodes.ClientInactive);
        await AssertRefusedAsync(auth, trialMember, SignInTarget.At(inactiveSlug), ProblemCodes.InvalidCredentials);   // not a member: nothing revealed
    }

    [DatabaseFact]
    public async Task The_public_card_shows_a_usable_laboratory_and_nothing_else()
    {
        var (activeId, activeSlug) = await LaboratoryAsync(Kind.Active);
        var (_, trialSlug) = await LaboratoryAsync(Kind.Trial);
        var (_, endedSlug) = await LaboratoryAsync(Kind.TrialEnded);
        var (_, inactiveSlug) = await LaboratoryAsync(Kind.Inactive);
        var anonymous = _factory.CreateClient();

        var card = await anonymous.GetAsync($"{Card}/{activeSlug.ToUpperInvariant()}");
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        Assert.Equal(new PublicLaboratory(activeSlug, "Lab " + activeSlug, "مختبر " + activeSlug, null), await card.Content.ReadFromJsonAsync<PublicLaboratory>());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"{Card}/{trialSlug}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"{Card}/{endedSlug}")).StatusCode);   // LIS-CR-012: its administrators still sign in there (to the wallet)

        foreach (var missing in new[] { inactiveSlug, "no-such-lab", "ab", "al_noor" })
        {
            var refused = await anonymous.GetAsync($"{Card}/{missing}");
            Assert.Equal((HttpStatusCode.NotFound, PlatformAdminCodes.LaboratoryAddressNotFound), (refused.StatusCode, await AuthClient.CodeOf(refused)));
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"{Card}/{missing}/logo")).StatusCode);
        }

        // no logo yet: 404; once uploaded, anonymous bytes with a public cache and the version in the ETag and on the card
        var noLogo = await anonymous.GetAsync($"{Card}/{activeSlug}/logo");
        Assert.Equal((HttpStatusCode.NotFound, PlatformAdminCodes.LaboratoryAddressNotFound), (noLogo.StatusCode, await AuthClient.CodeOf(noLogo)));
        var admin = await SignedInAsync(activeId, ["settings.manage"]);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "logo.png");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/v1/account/laboratory/logo", form)).StatusCode);

        var logo = await anonymous.GetAsync($"{Card}/{activeSlug}/logo");
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PngBytes, await logo.Content.ReadAsByteArrayAsync());
        Assert.Equal("public, max-age=300", logo.Headers.CacheControl!.ToString());
        Assert.Equal("\"logo-v1\"", logo.Headers.ETag!.Tag);
        Assert.Equal(1, (await anonymous.GetFromJsonAsync<PublicLaboratory>($"{Card}/{activeSlug}"))!.LogoVersion);
        Assert.True((await anonymous.GetAsync($"{Card}/{activeSlug}")).Headers.CacheControl!.NoStore);   // the card itself is never cached
    }

    [DatabaseFact]
    public async Task The_portal_address_of_a_laboratory_is_under_the_configured_base()
    {
        var (labId, slug) = await LaboratoryAsync(Kind.Active);
        var (staff, _, _) = await _factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["tenancy.view"]);
        var client = _factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(staff, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");

        var detail = await client.GetFromJsonAsync<ClientDetail>($"/api/v1/platform/clients/{labId}");
        Assert.Equal($"{Platform.Infrastructure.Security.ConfigGuard.DefaultPortalBaseUrl}/lab/{slug}", detail!.PortalUrl);
    }

    private static async Task AssertRefusedAsync(AuthClient auth, string username, SignInTarget target, string code)
    {
        var refused = await auth.SignInAsync(username, ApiFactory.Password, target);
        Assert.Equal((HttpStatusCode.Unauthorized, code), (refused.StatusCode, await AuthClient.CodeOf(refused)));
    }

    private async Task<HttpClient> SignedInAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await _factory.SeedUserAsync(tenantId: tenant, permissions: permissions);
        var client = _factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private enum Kind
    {
        Active,
        Trial,
        TrialEnded,
        Inactive,
    }

    /// <summary>A client laboratory with its own client record in the given state (reference data of its own).</summary>
    private async Task<(Guid Id, string Slug)> LaboratoryAsync(Kind kind)
    {
        var iso = TestCountryCodes.Next();
        var suffix = Guid.CreateVersion7().ToString("N");
        var country = Country.Create(new CountryData("Addressland " + iso, "أرض العناوين", iso, "+20", "Pound", "EGP", "en", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("A" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[..6], "باقة"), 100m, "EGP", 1, 5, 100, 1));
        var now = _factory.Clock.UtcNow;
        var slug = "addr-" + suffix[^10..];
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "01012345670", null, "Manager", "01012345671", null);
        var tenant = kind switch
        {
            Kind.Trial => Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Subscription, "en", 14, now),
            Kind.TrialEnded => Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Subscription, "en", 14, now.AddDays(-30)),
            _ => Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Manual, "en", 0, now),
        };
        if (kind == Kind.Inactive)
        {
            tenant.Deactivate("unpaid", now);
        }

        using var scope = _factory.Services.CreateScope();
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(TenantIds.Platform))
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
            scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        return (tenant.Id, slug);
    }
}
