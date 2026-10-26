using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Integration;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.PlatformAdmin;
using Platform.Application.Pricing;
using Platform.Application.Results;
using Platform.Application.Samples;
using Platform.Application.Tracker;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Contracts.Organization;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Organization;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;
using Platform.TestSupport;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Validation v0.5.0 (client set-up) over the real API: a restricted branch choice with no branch is refused ORG-018
/// instead of meaning "every branch" (B-02); paged registers sort on the server before paging, by an allow-listed key
/// (B-03); the session profile names the role in both languages (B-09).
/// </summary>
public sealed class SetupListsAndAccessFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Units = "/api/v1/catalogue/units";
    private const string Branches = "/api/v1/organization/branches";
    private const string Roles = "/api/v1/organization/roles";
    private const string Users = "/api/v1/organization/users";

    private static readonly string[] Administrator =
    [
        "organization.view", "organization.create", "organization.edit", "organization.manage",
        "identity.view", "identity.create", "identity.edit", "identity.manage",
        "catalogue.view", "catalogue.create", "catalogue.edit", "catalogue.manage",
        "results.view",
    ];

    /// <summary>The readers of the other paged registers of a laboratory (B-03 completion).</summary>
    private static readonly string[] Reader =
    [
        "catalogue.view", "organization.view", "patients.view", "pricing.view", "samples.view", "results.view", "integration.view",
    ];

    /// <summary>The Super Admin readers of the platform registers (clients, reference data, integration library); <c>tenancy.manage</c> creates the countries of the order test.</summary>
    private static readonly string[] PlatformReader = ["tenancy.view", "tenancy.manage", "notifications.view", "integration.view"];


    [DatabaseFact]
    public async Task A_restricted_branch_choice_without_a_branch_is_refused_on_create_and_on_assign()
    {
        var admin = await AdministratorAsync(Guid.CreateVersion7());
        var roleId = await CreateRoleAsync(admin, "Reception", "الاستقبال");
        var branch = await CreateBranchAsync(admin, "EAST");

        // B-02: "not every branch" and no branch used to store an empty list, which means every branch.
        var refused = await PostUserAsync(admin, $"none.{Suffix()}", roleId, Array.Empty<Guid>(), allBranches: false);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ORG-018"), (refused.StatusCode, await AuthClient.CodeOf(refused)));
        var contradictory = await PostUserAsync(admin, $"both.{Suffix()}", roleId, [branch], allBranches: true);
        Assert.Equal("ORG-018", await AuthClient.CodeOf(contradictory));

        var restricted = await PostUserAsync(admin, $"east.{Suffix()}", roleId, [branch], allBranches: false);
        Assert.Equal(HttpStatusCode.Created, restricted.StatusCode);
        var userId = (await restricted.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        Assert.Equal([branch], (await admin.GetFromJsonAsync<UserDetail>($"{Users}/{userId}"))!.BranchIds);

        var everyBranch = await PostUserAsync(admin, $"all.{Suffix()}", roleId, Array.Empty<Guid>(), allBranches: true);
        Assert.Equal(HttpStatusCode.Created, everyBranch.StatusCode);
        var legacy = await PostUserAsync(admin, $"old.{Suffix()}", roleId, Array.Empty<Guid>(), allBranches: null);
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);   // the older contract keeps "empty = every branch"

        var assignNone = await admin.PutAsJsonAsync($"{Users}/{userId}/branches", new AssignUserBranchesRequest([], AllBranches: false));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ORG-018"), (assignNone.StatusCode, await AuthClient.CodeOf(assignNone)));
        Assert.Equal([branch], (await admin.GetFromJsonAsync<UserDetail>($"{Users}/{userId}"))!.BranchIds);   // unchanged

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Users}/{userId}/branches", new AssignUserBranchesRequest([], AllBranches: true))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<UserDetail>($"{Users}/{userId}"))!.BranchIds);
    }

    [DatabaseFact]
    public async Task A_paged_register_is_sorted_on_the_server_before_it_is_paged()
    {
        var admin = await AdministratorAsync(Guid.CreateVersion7());
        var names = Enumerable.Range(1, 23).Select(index => $"U{index:D2}/L").ToList();
        foreach (var name in names)
        {
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(Units, new MeasurementUnitRequest(name))).StatusCode);
        }

        // B-03: page 2 of the descending list continues page 1 (the last three names), not a re-order of page 2 only.
        var descending = names.OrderByDescending(name => name, StringComparer.Ordinal).ToList();
        var first = await admin.GetFromJsonAsync<Paged<MeasurementUnitListItem>>($"{Units}?isActive=true&page=1&pageSize=20&sort=name&direction=desc");
        var second = await admin.GetFromJsonAsync<Paged<MeasurementUnitListItem>>($"{Units}?isActive=true&page=2&pageSize=20&sort=name&direction=desc");
        Assert.Equal(descending.Take(20), first!.Items.Select(unit => unit.Name));
        Assert.Equal(descending.Skip(20), second!.Items.Select(unit => unit.Name));
        Assert.Equal(23, second.TotalCount);

        var ascending = await admin.GetFromJsonAsync<Paged<MeasurementUnitListItem>>($"{Units}?page=2&pageSize=20&sort=NAME&direction=asc");
        Assert.Equal(names.Skip(20), ascending!.Items.Select(unit => unit.Name));
        var unsorted = await admin.GetFromJsonAsync<Paged<MeasurementUnitListItem>>($"{Units}?page=1&pageSize=3");
        Assert.Equal(names.Take(3), unsorted!.Items.Select(unit => unit.Name));   // the register's own order without a sort

        // Only the allow-listed columns sort; anything else is a validation refusal, never a raw column name.
        var unknown = await admin.GetAsync($"{Units}?sort=id&direction=desc");
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (unknown.StatusCode, await AuthClient.CodeOf(unknown)));
        var direction = await admin.GetAsync($"{Units}?sort=name&direction=down");
        Assert.Equal(HttpStatusCode.BadRequest, direction.StatusCode);
    }

    /// <summary>
    /// R-09: every list orders its text columns case-insensitively. The database collation is <c>C</c> (byte order), so
    /// without <c>lower()</c> "eGFR" and "hCG" land after "Sodium"; the tie-breaker keeps equal names in a stable order.
    /// </summary>
    [DatabaseFact]
    public async Task A_text_column_sorts_without_regard_to_case_in_every_register()
    {
        var admin = await AdministratorAsync(Guid.CreateVersion7());
        var created = new (string Code, string NameEn)[] { ("RNA", "Sodium"), ("RNB", "hCG"), ("RNC", "eGFR"), ("RND", "Albumin") };
        foreach (var (code, nameEn) in created)
        {
            var response = await admin.PostAsJsonAsync("/api/v1/catalogue/sample-types", new CreateSampleTypeRequest(code, code, nameEn, "عينة " + code, null));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await CreateBranchAsync(admin, code, nameEn);
        }

        var ascending = await admin.GetFromJsonAsync<Paged<SampleTypeListItem>>("/api/v1/catalogue/sample-types?sort=nameEn&direction=asc");
        Assert.Equal(["Albumin", "eGFR", "hCG", "Sodium"], ascending!.Items.Select(item => item.NameEn));
        var descending = await admin.GetFromJsonAsync<Paged<SampleTypeListItem>>("/api/v1/catalogue/sample-types?sort=nameEn&direction=desc&pageSize=2&page=1");
        Assert.Equal(["Sodium", "hCG"], descending!.Items.Select(item => item.NameEn));

        var branches = await admin.GetFromJsonAsync<Paged<BranchListItem>>($"{Branches}?sort=nameEn&direction=asc");
        Assert.Equal(["Albumin", "eGFR", "hCG", "Sodium"], branches!.Items.Select(branch => branch.NameEn));
    }

    [DatabaseFact]
    public async Task Branches_and_users_sort_by_their_allow_listed_columns()
    {
        var admin = await AdministratorAsync(Guid.CreateVersion7());
        await CreateBranchAsync(admin, "BBB", "Zamalek", "الزمالك");
        await CreateBranchAsync(admin, "AAA", "Maadi", "المعادي");
        await CreateBranchAsync(admin, "CCC", "Heliopolis", "مصر الجديدة");

        var byCode = await admin.GetFromJsonAsync<Paged<BranchListItem>>($"{Branches}?sort=code&direction=desc");
        Assert.Equal(["CCC", "BBB", "AAA"], byCode!.Items.Select(branch => branch.Code));
        var byName = await admin.GetFromJsonAsync<Paged<BranchListItem>>($"{Branches}?sort=nameEn&pageSize=2&page=1");
        Assert.Equal(["Heliopolis", "Maadi"], byName!.Items.Select(branch => branch.NameEn));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Branches}?sort=email")).StatusCode);

        var roleId = await CreateRoleAsync(admin, "Clerk", "كاتب");
        var suffix = Suffix();
        foreach (var prefix in new[] { "b", "c", "a" })
        {
            Assert.Equal(HttpStatusCode.Created, (await PostUserAsync(admin, $"{prefix}.{suffix}", roleId, Array.Empty<Guid>(), allBranches: true)).StatusCode);
        }

        var users = await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={suffix}&sort=username&direction=desc");
        Assert.Equal([$"c.{suffix}", $"b.{suffix}", $"a.{suffix}"], users!.Items.Select(user => user.Username));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Users}?sort=password")).StatusCode);

        // The search text is literal: "_" is escaped and the escape character reaches ILIKE (an unescaped "_" would also
        // match "uz…", and without the ESCAPE clause the escaped pattern matched nothing).
        Assert.Equal(HttpStatusCode.Created, (await PostUserAsync(admin, $"u_{suffix}", roleId, Array.Empty<Guid>(), allBranches: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostUserAsync(admin, $"uz{suffix}", roleId, Array.Empty<Guid>(), allBranches: true)).StatusCode);
        var literal = await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search=u_{suffix}");
        Assert.Equal([$"u_{suffix}"], literal!.Items.Select(user => user.Username));
        await CreateBranchAsync(admin, "B_1", "Under_score", "شرطة_سفلية");
        await CreateBranchAsync(admin, "BX1", "Underxscore", "بلا شرطة");
        var branches = await admin.GetFromJsonAsync<Paged<BranchListItem>>($"{Branches}?search=under_");
        Assert.Equal(["B_1"], branches!.Items.Select(branch => branch.Code));
    }

    /// <summary>
    /// Every allow-listed key of every sortable register reaches a column the database orders by, in both directions (a
    /// gate: a key without an expression, or an expression the provider cannot translate, fails here).
    /// </summary>
    [DatabaseFact]
    public async Task Every_allow_listed_sort_key_of_every_register_is_served()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);
        await admin.PostAsJsonAsync(Units, new MeasurementUnitRequest("mg/dL"));
        await CreateBranchAsync(admin, "MAIN");

        var registers = new (string Path, SortKeys Keys)[]
        {
            ("/api/v1/catalogue/clinical-groups", CatalogueSortKeys.ClinicalGroups),
            ("/api/v1/catalogue/sample-types", CatalogueSortKeys.SampleTypes),
            ("/api/v1/catalogue/sample-compatibility-groups", CatalogueSortKeys.SampleCompatibilityGroups),   // LIS-CR-015 M4 (groups)
            (Units, CatalogueSortKeys.Units),
            ("/api/v1/catalogue/patient-conditions", CatalogueSortKeys.PatientConditions),
            ("/api/v1/catalogue/services", CatalogueSortKeys.Services),
            ("/api/v1/catalogue/comment-templates", CatalogueSortKeys.CommentTemplates),
            ("/api/v1/catalogue/microbiology/drug-types", CatalogueSortKeys.DrugTypes),
            ("/api/v1/catalogue/microbiology/commercial-names", CatalogueSortKeys.CommercialNames),
            ("/api/v1/catalogue/microbiology/antibiotics", CatalogueSortKeys.Antibiotics),
            ("/api/v1/catalogue/microbiology/organisms", CatalogueSortKeys.Organisms),
            ("/api/v1/catalogue/microbiology/sensitivity-values", CatalogueSortKeys.SensitivityValues),
            ("/api/v1/catalogue/reasons", CatalogueSortKeys.Reasons),
            ("/api/v1/catalogue/report-templates", CatalogueSortKeys.ReportTemplates),
            ("/api/v1/catalogue/sending-labs", CatalogueSortKeys.SendingLabs),
            (Branches, OrganizationSortKeys.Branches),
            (Users, OrganizationSortKeys.Users),
        };
        await ServesEverySortKeyAsync(admin, registers);

        // The other paged registers of a laboratory: catalogue order aids and tests, roles, patients, pricing,
        // samples, the worklist (a reader with result rights, so the sorted query runs) and the integration registers.
        var (readerName, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: Reader, displayName: "Register Reader");
        await GrantResultRightsAsync(tenant, readerName);
        var reader = await SignedInAsync(readerName);
        await ServesEverySortKeyAsync(reader,
        [
            ("/api/v1/catalogue/tests", CatalogueSortKeys.LabTests),
            ("/api/v1/catalogue/preparations", CatalogueSortKeys.Preparations),
            ("/api/v1/catalogue/doctors", CatalogueSortKeys.Doctors),
            ("/api/v1/catalogue/diagnoses", CatalogueSortKeys.Diagnoses),
            ("/api/v1/catalogue/shortcut-sets", CatalogueSortKeys.ShortcutSets),
            ("/api/v1/catalogue/test-rules", CatalogueSortKeys.TestRules),
            (Roles, OrganizationSortKeys.Roles),
            ("/api/v1/patients", PatientSortKeys.Patients),
            ("/api/v1/pricing/payers", PricingSortKeys.Payers),
            ("/api/v1/pricing/base-price-lists", PricingSortKeys.BasePriceLists),
            ("/api/v1/samples", SamplesSortKeys.Samples),
            ("/api/v1/results/worklist", ResultsSortKeys.Worklist),
            ("/api/v1/integration/analysers", IntegrationSortKeys.Analysers),
            ("/api/v1/integration/pcr-kits", IntegrationSortKeys.PcrKits),
            ("/api/v1/integration/site-agents", IntegrationSortKeys.SiteAgents),
        ]);

        // The Lab Tracker (LIS-CR-006) reads the session branch: a reader assigned to one branch, which the session selects.
        var (trackerName, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["registration.view"], displayName: "Tracker Reader", branchIds: [Guid.CreateVersion7()]);
        await ServesEverySortKeyAsync(await SignedInAsync(trackerName), [("/api/v1/tracker/visits", TrackerSortKeys.Visits)]);

        // The Super Admin registers that page on the server (a platform-tenant reader).
        var (platformName, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: PlatformReader, displayName: "Platform Reader");
        var platform = await SignedInAsync(platformName);
        await ServesEverySortKeyAsync(platform,
        [
            ("/api/v1/platform/clients", PlatformAdminSortKeys.Clients),
            ("/api/v1/platform/countries", PlatformAdminSortKeys.Countries),
            ("/api/v1/platform/governorates", PlatformAdminSortKeys.Governorates),
            ("/api/v1/platform/notification-templates", PlatformAdminSortKeys.NotificationTemplates),
            ("/api/v1/platform/library/analyser-models", PlatformAdminSortKeys.AnalyserModels),
            ("/api/v1/platform/library/pcr-kits", PlatformAdminSortKeys.PcrKits),
            ("/api/v1/platform/library/code-systems", PlatformAdminSortKeys.CodeSystems),
            ("/api/v1/platform/library/rule-templates", PlatformAdminSortKeys.RuleTemplates),
        ]);
    }

    /// <summary>
    /// A register whose rows are joined after paging (governorates with their country) keeps the server order: page 2 of
    /// the Arabic-name order continues page 1, and the country column sorts too (B-03).
    /// </summary>
    [DatabaseFact]
    public async Task A_joined_register_keeps_the_server_order_across_pages()
    {
        var (platformName, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: PlatformReader, displayName: "Platform Reader");
        var platform = await SignedInAsync(platformName);
        var countryId = await CreateCountryAsync(platform);
        var governorates = new (string En, string Ar)[] { ("Sort Alpha", "ي محافظة"), ("Sort Beta", "ب محافظة"), ("Sort Gamma", "م محافظة") };
        foreach (var (en, ar) in governorates)
        {
            Assert.Equal(HttpStatusCode.Created, (await platform.PostAsJsonAsync("/api/v1/platform/governorates", new GovernorateRequest(countryId, en, ar, null, null))).StatusCode);
        }

        var expected = governorates.OrderByDescending(item => item.Ar, StringComparer.Ordinal).Select(item => item.En).ToList();
        var shown = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var slice = await platform.GetFromJsonAsync<Paged<GovernorateListItem>>($"/api/v1/platform/governorates?countryId={countryId}&sort=nameAr&direction=desc&page={page}&pageSize=1");
            Assert.Equal(3, slice!.TotalCount);
            shown.Add(Assert.Single(slice.Items).NameEn);
        }

        Assert.Equal(expected, shown);
        var byCountry = await platform.GetFromJsonAsync<Paged<GovernorateListItem>>($"/api/v1/platform/governorates?countryId={countryId}&sort=countryEn&direction=asc&pageSize=2&page=2");
        Assert.Equal(["Sort Gamma"], byCountry!.Items.Select(item => item.NameEn));   // one country: the name breaks the tie
    }

    private static async Task ServesEverySortKeyAsync(HttpClient client, IReadOnlyList<(string Path, SortKeys Keys)> registers)
    {
        foreach (var (path, keys) in registers)
        {
            foreach (var key in keys.Names)
            {
                foreach (var direction in new[] { "asc", "desc" })
                {
                    var response = await client.GetAsync($"{path}?sort={key}&direction={direction}&page=1&pageSize=5");
                    Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} sort={key} {direction}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                }
            }

            var unknown = await client.GetAsync($"{path}?sort=not-a-column");
            Assert.True(unknown.StatusCode == HttpStatusCode.BadRequest, $"{path} sort=not-a-column: {(int)unknown.StatusCode}");
        }
    }

    /// <summary>A result right on a clinical group: the worklist reads results only for a user who may see some (its role is <c>role-&lt;username&gt;</c>).</summary>
    private async Task GrantResultRightsAsync(Guid tenant, string username)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var role = (await scope.ServiceProvider.GetRequiredService<IRoleRepository>().FindByNameAsync($"role-{username}", CancellationToken.None))!;
        role.SetResultRights([new ResultRight(Guid.CreateVersion7(), null, true, true, true, true, true)], factory.Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>A country with a free two-letter code (countries are platform-wide, so another test may hold a code).</summary>
    private static async Task<Guid> CreateCountryAsync(HttpClient platform)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var iso = TestCountryCodes.Next();
            var created = await platform.PostAsJsonAsync("/api/v1/platform/countries", new CountryRequest("Sortland " + iso, "أرض الترتيب", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
            if (created.StatusCode == HttpStatusCode.Created)
            {
                return (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
            }
        }

        throw new InvalidOperationException("No free country code for the test.");
    }

    private async Task<HttpClient> SignedInAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    [DatabaseFact]
    public async Task The_session_profile_names_the_role_in_both_languages()
    {
        var admin = await AdministratorAsync(Guid.CreateVersion7());
        var roleId = await CreateRoleAsync(admin, "Laboratory Technician", "فني المختبر");
        var username = $"tech.{Suffix()}";
        Assert.Equal(HttpStatusCode.Created, (await PostUserAsync(admin, username, roleId, Array.Empty<Guid>(), allBranches: true)).StatusCode);

        // B-09: the shell shows the role in the language of the page; sign-in and refresh carry both names.
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, Password);
        Assert.Equal(("Laboratory Technician", "فني المختبر"), (body.Profile!.RoleName, body.Profile.RoleNameAr));
    }

    [DatabaseFact]
    public async Task The_session_profile_names_the_user_in_both_languages()
    {
        var admin = await AdministratorAsync(Guid.CreateVersion7());
        var roleId = await CreateRoleAsync(admin, "Reception", "الاستقبال");
        var username = $"rcpt.{Suffix()}";
        Assert.Equal(HttpStatusCode.Created, (await PostUserAsync(admin, username, roleId, Array.Empty<Guid>(), allBranches: true)).StatusCode);

        // R-12: the shell names the user in the language of the page; the membership's full name in both languages
        // travels with sign-in and refresh, and the preferred-language name stays in DisplayName.
        var client = factory.CreateClient();
        var auth = new AuthClient(client);
        var (body, cookie, _) = await auth.SignInExpectingSuccessAsync(username, Password);
        Assert.Equal(("Staff " + username, "موظف"), (body.Profile!.DisplayNameEn, body.Profile.DisplayNameAr));
        Assert.Equal("Staff " + username, body.Profile.DisplayName);

        var refreshed = await auth.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var profile = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!;
        Assert.Equal(("Staff " + username, "موظف"), (profile.DisplayNameEn, profile.DisplayNameAr));
    }

    private const string Password = "a long staff password";

    private async Task<HttpClient> AdministratorAsync(Guid tenant)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: Administrator, displayName: "Set-up Administrator");
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private static async Task<Guid> CreateRoleAsync(HttpClient admin, string nameEn, string nameAr)
    {
        var created = await admin.PostAsJsonAsync(Roles, new CreateRoleRequest(nameEn, nameAr, null, ["results.view"]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static async Task<Guid> CreateBranchAsync(HttpClient admin, string code, string? nameEn = null, string? nameAr = null)
    {
        var created = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest(code, nameEn ?? code + " branch", nameAr ?? "فرع " + code, null, null, null, null, "EGP"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    /// <summary>Creates a user with the password sealed as the SPA seals it (D-03).</summary>
    private static async Task<HttpResponseMessage> PostUserAsync(HttpClient admin, string username, Guid roleId, IReadOnlyList<Guid> branchIds, bool? allBranches)
    {
        var challenge = await new AuthClient(admin).ChallengeAsync();
        var sealedPassword = TestSealer.Seal(challenge, "password", Password, "POST " + Users);
        return await admin.PostAsJsonAsync(Users, new
        {
            username,
            password = sealedPassword,
            mustChangePassword = false,
            fullNameEn = "Staff " + username,
            fullNameAr = "موظف",
            email = $"{username}@lab.example",
            roleId,
            branchIds,
            allBranches,
        });
    }

    private static string Suffix() => Guid.CreateVersion7().ToString("N")[^8..];
}
