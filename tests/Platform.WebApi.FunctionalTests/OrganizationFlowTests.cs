using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Identity;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Contracts.Organization;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;
using Platform.TestSupport;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The Organization pages over the real API (intake LIS-INT-ORG-001 §16): branches (CP-36), roles with special and
/// result rights (CP-37, BR-17), users with enveloped passwords, lock, reset and overrides (CP-38), and the branch
/// selection of an unrestricted membership (CP-04). Every refusal arrives as its code.
/// </summary>
public sealed class OrganizationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Branches = "/api/v1/organization/branches";
    private const string Roles = "/api/v1/organization/roles";
    private const string Users = "/api/v1/organization/users";
    private const string MyPrivileges = "/api/v1/organization/me/privileges";
    private const string SelectBranch = "/api/v1/auth/select-branch";

    private static readonly string[] AdministratorPermissions =
    [
        "organization.view", "organization.create", "organization.edit", "organization.manage",
        "identity.view", "identity.create", "identity.edit", "identity.manage",
        "registration.view", "registration.create", "patients.view", "results.view",
    ];

    [DatabaseFact]
    public async Task Branches_are_created_updated_and_deactivated_but_the_last_active_one_stays()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["patients.view"]);
        var outsiderClient = await SignedInClientAsync(outsider);

        var created = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest(" nasr ", "Nasr City", "مدينة نصر", null, null, "+20 2 2222 3333", "nasr@lab.example", "egp"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var nasr = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var duplicate = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest("NASR", "Again", "مرة أخرى", null, null, null, null, "EGP"));
        Assert.Equal(("ORG-100", HttpStatusCode.Conflict), (await AuthClient.CodeOf(duplicate), duplicate.StatusCode));

        var shape = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest("", "No code", "بدون رمز", null, null, null, null, "EGP"));
        Assert.Equal(HttpStatusCode.BadRequest, shape.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Branches}/{nasr}", new UpdateBranchRequest("Nasr City 1", "مدينة نصر 1", null, null, null, null, "USD"))).StatusCode);
        var listed = await admin.GetFromJsonAsync<Paged<BranchListItem>>(Branches + "?search=nasr");
        Assert.Equal(("NASR", "Nasr City 1", "USD"), (listed!.Items[0].Code, listed.Items[0].NameEn, listed.Items[0].Currency));

        var last = await admin.PostAsync($"{Branches}/{nasr}/deactivate", null);
        Assert.Equal(("ORG-110", HttpStatusCode.Conflict), (await AuthClient.CodeOf(last), last.StatusCode));

        await admin.PostAsJsonAsync(Branches, new CreateBranchRequest("MAADI", "Maadi", "المعادي", null, null, null, null, "EGP"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Branches}/{nasr}/deactivate", null)).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<BranchListItem>($"{Branches}/{nasr}"))!.IsActive);

        var forbidden = await outsiderClient.GetAsync(Branches);
        Assert.Equal((HttpStatusCode.Forbidden, ProblemCodes.Forbidden), (forbidden.StatusCode, await AuthClient.CodeOf(forbidden)));
    }

    [DatabaseFact]
    public async Task A_role_carries_special_and_result_rights_and_a_change_reaches_its_members_at_once()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);

        var roleId = await CreateRoleAsync(admin, "Reception", ["registration.view"]);
        var duplicate = await admin.PostAsJsonAsync(Roles, new CreateRoleRequest("RECEPTION", "مكرر", null, ["registration.view"]));
        Assert.Equal(("ORG-101", HttpStatusCode.Conflict), (await AuthClient.CodeOf(duplicate), duplicate.StatusCode));

        var platformKey = await admin.PostAsJsonAsync(Roles, new CreateRoleRequest("Sneaky", "متسلل", null, ["tenancy.manage"]));
        Assert.Equal(HttpStatusCode.Forbidden, platformKey.StatusCode);
        Assert.Contains(await AuthClient.CodeOf(platformKey), new[] { "ORG-033", "ORG-030" });

        var badCap = await admin.PutAsJsonAsync($"{Roles}/{roleId}/special-rights", new SpecialRightsDto(101, true, false, false, false, false, false, false, false));
        Assert.Equal(HttpStatusCode.BadRequest, badCap.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Roles}/{roleId}/special-rights", new SpecialRightsDto(10, true, true, false, false, false, false, true, false))).StatusCode);
        var group = Guid.CreateVersion7();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Roles}/{roleId}/result-rights", new SetRoleResultRightsRequest([new ResultRightDto(group, null, false, false, true, false, false)]))).StatusCode);

        var (memberName, memberPassword) = await CreateUserAsync(admin, roleId);
        var member = await SignedInClientAsync(memberName, memberPassword);
        var privileges = await member.GetFromJsonAsync<MyPrivilegesResponse>(MyPrivileges);
        Assert.Equal(["registration.view"], privileges!.Privileges.PermissionKeys);
        Assert.Equal((10, true), (privileges.Privileges.SpecialRights.DiscountCapPercent, privileges.Privileges.SpecialRights.CanRefund));
        Assert.True(Assert.Single(privileges.Privileges.ResultRights).CanSee);   // verify implies see

        // BR-17: the role changes, the member's very next request carries the new permission.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Roles}/{roleId}/permissions", new SetRolePermissionsRequest(["registration.view", "registration.create"]))).StatusCode);
        var me = await member.GetFromJsonAsync<SessionResponse>(AuthClient.MePath);
        Assert.Contains("registration.create", me!.Permissions);

        var withMembers = await admin.PostAsync($"{Roles}/{roleId}/deactivate", null);
        Assert.Equal(("ORG-011", HttpStatusCode.Conflict), (await AuthClient.CodeOf(withMembers), withMembers.StatusCode));

        var detail = await admin.GetFromJsonAsync<RoleDetail>($"{Roles}/{roleId}");
        Assert.Equal((1, 2), (detail!.MemberCount, detail.PermissionKeys.Count));
    }

    [DatabaseFact]
    public async Task Users_are_created_with_an_enveloped_password_locked_unlocked_and_reset()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);
        var roleId = await CreateRoleAsync(admin, "Technician", ["results.view"]);

        var plaintext = await admin.PostAsJsonAsync(Users, UserBody($"plain.{Suffix()}", "a plaintext password", roleId));
        Assert.Equal(ProblemCodes.CredentialPlaintext, await AuthClient.CodeOf(plaintext));

        var tooShort = await PostUserAsync(admin, $"short.{Suffix()}", "short", roleId);
        Assert.Equal("ORG-020", await AuthClient.CodeOf(tooShort));

        var (username, password) = await CreateUserAsync(admin, roleId);
        var userId = (await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={username}"))!.Items.Single().UserId;
        await SignedInClientAsync(username, password);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Users}/{userId}/lock", null)).StatusCode);
        var lockedOut = await new AuthClient(factory.CreateClient()).SignInAsync(username, password);
        Assert.Equal(ProblemCodes.AccountLockedByAdministrator, await AuthClient.CodeOf(lockedOut));   // B-11: an administrator's lock has its own code
        Assert.Equal("Locked", (await admin.GetFromJsonAsync<UserDetail>($"{Users}/{userId}"))!.Status);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Users}/{userId}/unlock", null)).StatusCode);
        await SignedInClientAsync(username, password);

        var temporary = "temporary pass " + Suffix();
        var reset = await PostSealedAsync(admin, $"{Users}/{userId}/reset-password", "temporaryPassword", temporary, sealedValue => new { temporaryPassword = sealedValue, mustChangePassword = true });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(ProblemCodes.InvalidCredentials, await AuthClient.CodeOf(await new AuthClient(factory.CreateClient()).SignInAsync(username, password)));
        var afterReset = await SignedInClientAsync(username, temporary);
        Assert.True((await afterReset.GetFromJsonAsync<MyPrivilegesResponse>(MyPrivileges))!.MustChangePassword);

        var adminId = (await admin.GetFromJsonAsync<SessionResponse>(AuthClient.MePath))!.UserId;
        var self = await admin.PostAsync($"{Users}/{adminId}/lock", null);
        Assert.Equal(("SOD-ORG-001", HttpStatusCode.Forbidden), (await AuthClient.CodeOf(self), self.StatusCode));
    }

    [DatabaseFact]
    public async Task Overrides_affect_one_user_restore_returns_to_the_role_and_nobody_grants_beyond_their_own_rights()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);
        var roleId = await CreateRoleAsync(admin, "Clerk", ["registration.view"]);
        var (username, password) = await CreateUserAsync(admin, roleId);
        var userId = (await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={username}"))!.Items.Single().UserId;
        var member = await SignedInClientAsync(username, password);

        var overridden = await admin.PutAsJsonAsync($"{Users}/{userId}/overrides", new SetUserOverridesRequest(["patients.view"], [], null));
        Assert.Equal(HttpStatusCode.NoContent, overridden.StatusCode);
        Assert.Contains("patients.view", (await member.GetFromJsonAsync<MyPrivilegesResponse>(MyPrivileges))!.Privileges.PermissionKeys);
        var listed = (await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={username}"))!.Items.Single();
        Assert.True(listed.IsModified);

        var beyond = await admin.PutAsJsonAsync($"{Users}/{userId}/overrides", new SetUserOverridesRequest(["billing.approve"], [], null));
        Assert.Equal(("ORG-030", HttpStatusCode.Forbidden), (await AuthClient.CodeOf(beyond), beyond.StatusCode));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Users}/{userId}/restore", null)).StatusCode);
        Assert.DoesNotContain("patients.view", (await member.GetFromJsonAsync<MyPrivilegesResponse>(MyPrivileges))!.Privileges.PermissionKeys);
        Assert.False((await admin.GetFromJsonAsync<UserDetail>($"{Users}/{userId}"))!.IsModified);
    }

    [DatabaseFact]
    public async Task The_users_list_names_the_assigned_branches_by_code_for_the_export()
    {
        // UAT R1-06: the users CSV wrote the branch count ("2") where the codes belong.
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);
        var north = await CreateBranchAsync(admin, "NORTH");
        var main = await CreateBranchAsync(admin, "MAIN");
        var roleId = await CreateRoleAsync(admin, "Clerk", ["registration.view"]);
        var (restricted, _) = await CreateUserAsync(admin, roleId);
        var (unrestricted, _) = await CreateUserAsync(admin, roleId);
        var userId = (await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={restricted}"))!.Items.Single().UserId;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Users}/{userId}/branches", new AssignUserBranchesRequest([north, main]))).StatusCode);

        var listed = (await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={restricted}"))!.Items.Single();
        Assert.Equal(2, listed.BranchCount);
        Assert.Equal(["MAIN", "NORTH"], listed.BranchCodes);
        Assert.Empty((await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={unrestricted}"))!.Items.Single().BranchCodes!);
    }

    [DatabaseFact]
    public async Task An_unrestricted_member_selects_any_active_branch_and_a_single_active_branch_is_selected_at_sign_in()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);
        var main = await CreateBranchAsync(admin, "MAIN");
        var (member, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["patients.view"]);   // no branch assigned = every active branch

        var (single, _, _) = await new AuthClient(factory.CreateClient()).SignInExpectingSuccessAsync(member, ApiFactory.Password);
        Assert.Equal(main, single.Profile!.BranchId);   // the only active branch needs no selection step

        var second = await CreateBranchAsync(admin, "SECOND");
        var client = await SignedInClientAsync(member);
        var selectable = await client.GetFromJsonAsync<SelectableBranchesResponse>($"{Branches}/selectable");
        Assert.Equal(new[] { main, second }.Order(), selectable!.Branches.Select(branch => branch.Id).Order());

        var chosen = await client.PostAsJsonAsync(SelectBranch, new SelectBranchRequest(second));
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        Assert.Equal(second, (await chosen.Content.ReadFromJsonAsync<AuthProfile>())!.BranchId);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Branches}/{main}/deactivate", null)).StatusCode);
        var inactive = await client.PostAsJsonAsync(SelectBranch, new SelectBranchRequest(main));
        Assert.Equal((HttpStatusCode.Forbidden, ProblemCodes.BranchNotAssigned), (inactive.StatusCode, await AuthClient.CodeOf(inactive)));

        var foreign = await client.PostAsJsonAsync(SelectBranch, new SelectBranchRequest(Guid.CreateVersion7()));
        Assert.Equal(ProblemCodes.BranchNotAssigned, await AuthClient.CodeOf(foreign));
    }

    [DatabaseFact]
    public async Task A_branch_refuses_a_governorate_of_another_country_and_names_the_field_of_a_bad_contact()
    {
        var platform = await PlatformAdministratorAsync();
        var (egypt, alexandria) = await CreatePlaceAsync(platform, "Alexandria");
        var (elsewhere, _) = await CreatePlaceAsync(platform, "Capital");
        var admin = await AdministratorAsync(Guid.CreateVersion7());

        // UAT B-02: the pair is checked on the server, on create and on update (ORG-017, a field refusal: 422)
        var wrongPair = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest("NORTH", "North", "الشمال", elsewhere, alexandria, null, null, "EGP"));
        Assert.Equal(("ORG-017", HttpStatusCode.UnprocessableEntity), (await AuthClient.CodeOf(wrongPair), wrongPair.StatusCode));
        var unknown = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest("NORTH", "North", "الشمال", egypt, Guid.CreateVersion7(), null, null, "EGP"));
        Assert.Equal("ORG-017", await AuthClient.CodeOf(unknown));
        var withoutCountry = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest("NORTH", "North", "الشمال", null, alexandria, null, null, "EGP"));
        Assert.Equal("ORG-008", await AuthClient.CodeOf(withoutCountry));

        var created = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest("NORTH", "North", "الشمال", egypt, alexandria, null, null, "EGP"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var north = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var moved = await admin.PutAsJsonAsync($"{Branches}/{north}", new UpdateBranchRequest("North", "الشمال", elsewhere, alexandria, null, null, "EGP"));
        Assert.Equal(("ORG-017", HttpStatusCode.UnprocessableEntity), (await AuthClient.CodeOf(moved), moved.StatusCode));
        var stored = await admin.GetFromJsonAsync<BranchListItem>($"{Branches}/{north}");
        Assert.Equal((egypt, alexandria), (stored!.CountryId, stored.GovernorateId));

        // UAT B-03: one code per field, so the page marks the field
        var email = await admin.PutAsJsonAsync($"{Branches}/{north}", new UpdateBranchRequest("North", "الشمال", egypt, alexandria, null, "not-an-email", "EGP"));
        Assert.Equal(("ORG-016", HttpStatusCode.UnprocessableEntity), (await AuthClient.CodeOf(email), email.StatusCode));
        var phone = await admin.PutAsJsonAsync($"{Branches}/{north}", new UpdateBranchRequest("North", "الشمال", egypt, alexandria, "phone!", null, "EGP"));
        Assert.Equal("ORG-015", await AuthClient.CodeOf(phone));
    }

    [DatabaseFact]
    public async Task The_permission_catalogue_of_a_laboratory_offers_no_platform_module()
    {
        var admin = await AdministratorAsync(Guid.CreateVersion7());

        var catalogue = await admin.GetFromJsonAsync<PermissionCatalogueResponse>("/api/v1/organization/permission-catalogue");

        Assert.DoesNotContain(catalogue!.Modules, module => module.Key is "platform" or "tenancy");
        Assert.Contains(catalogue.Modules, module => module.Key == "registration" && module.Actions.Contains("create"));
        Assert.Contains("viewConfidential", catalogue.Actions);
    }

    /// <summary>
    /// Validation 2026-10-11, C-01: the Platform Administrator holds every catalogued key (seeder, and migration
    /// GrantExportPermissions for existing databases), so it grants the export keys v0.4.0 introduced; an administrator who
    /// does not hold a key still cannot hand it out (INV-ORG-011, ORG-030), which is why nobody could grant them before.
    /// </summary>
    [DatabaseFact]
    public async Task The_platform_administrator_grants_the_export_keys_that_a_holder_without_them_cannot_C_01()
    {
        var username = $"nt.{Suffix()}";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdministratorSeeder>().SeedAsync(username, ApiFactory.Password, CancellationToken.None);
        }

        var platform = await SignedInClientAsync(username);
        var roleId = await CreateRoleAsync(platform, $"Exporters {Suffix()}", ["organization.view"]);
        var granted = await platform.PutAsJsonAsync($"{Roles}/{roleId}/permissions", new SetRolePermissionsRequest(["organization.view", "organization.export", "treasury.export"]));
        Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
        Assert.Equal(["organization.export", "organization.view", "treasury.export"], (await platform.GetFromJsonAsync<RoleDetail>($"{Roles}/{roleId}"))!.PermissionKeys.Order(StringComparer.Ordinal));

        var (supervisor, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["organization.view", "organization.edit", "organization.manage"], displayName: "Platform Supervisor");
        var otherRoleId = await CreateRoleAsync(platform, $"Viewers {Suffix()}", ["organization.view"]);
        var refused = await (await SignedInClientAsync(supervisor)).PutAsJsonAsync($"{Roles}/{otherRoleId}/permissions", new SetRolePermissionsRequest(["organization.view", "organization.export"]));
        Assert.Equal(("ORG-030", HttpStatusCode.Forbidden), (await AuthClient.CodeOf(refused), refused.StatusCode));
    }

    [DatabaseFact]
    public async Task A_user_mobile_is_stored_in_E164_and_unique_in_every_spelling_C_03()
    {
        await CreatePlaceAsync(await PlatformAdministratorAsync(), "Mobile " + Suffix());   // an active +20 country, ten national digits
        var tenant = Guid.CreateVersion7();
        var admin = await AdministratorAsync(tenant);
        var roleId = await CreateRoleAsync(admin, "Reception", ["registration.view"]);
        var national = "010" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var first = $"mob.{Suffix()}";
        var created = await PostSealedAsync(admin, Users, "password", "a long staff password", sealedValue => WithMobile(UserBody(first, sealedValue, roleId), "+2" + national));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var firstId = (await admin.GetFromJsonAsync<Paged<UserListItem>>($"{Users}?search={first}"))!.Items.Single().UserId;
        Assert.Equal("+2" + national, (await admin.GetFromJsonAsync<UserDetail>($"{Users}/{firstId}"))!.Mobile);

        // The same number in its national spelling is the same number: refused, not stored a second time.
        var duplicate = await PostSealedAsync(admin, Users, "password", "a long staff password", sealedValue => WithMobile(UserBody($"mob.{Suffix()}", sealedValue, roleId), national));
        Assert.Equal(("ORG-104", HttpStatusCode.Conflict), (await AuthClient.CodeOf(duplicate), duplicate.StatusCode));
    }

    private static Dictionary<string, object?> WithMobile(object body, string mobile)
    {
        var values = body.GetType().GetProperties().ToDictionary(property => property.Name, property => property.GetValue(body));
        values["mobile"] = mobile;
        return values;
    }


    private async Task<HttpClient> PlatformAdministratorAsync()
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["tenancy.view", "tenancy.manage"], displayName: "Geography Admin");
        return await SignedInClientAsync(username);
    }

    /// <summary>A country with one governorate, through the platform API (ISO codes "Q?" and "W?" are this class's own).</summary>
    private static async Task<(Guid Country, Guid Governorate)> CreatePlaceAsync(HttpClient platform, string governorate)
    {
        var iso = TestCountryCodes.Next();
        var country = await platform.PostAsJsonAsync("/api/v1/platform/countries", new CountryRequest("Branchland " + iso, "أرض الفروع", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        Assert.Equal(HttpStatusCode.Created, country.StatusCode);
        var countryId = (await country.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var created = await platform.PostAsJsonAsync("/api/v1/platform/governorates", new GovernorateRequest(countryId, governorate, "محافظة", null, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (countryId, (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id);
    }

    /// <summary>The random tail of a version-7 UUID: its leading characters are the millisecond clock and repeat for about a minute.</summary>
    private static string Suffix() => Guid.CreateVersion7().ToString("N")[^8..];

    private async Task<HttpClient> AdministratorAsync(Guid tenant)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: AdministratorPermissions, displayName: "Lab Administrator");
        return await SignedInClientAsync(username);
    }

    private static async Task<Guid> CreateRoleAsync(HttpClient admin, string name, string[] keys)
    {
        var created = await admin.PostAsJsonAsync(Roles, new CreateRoleRequest(name, name + " ع", null, keys));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static async Task<Guid> CreateBranchAsync(HttpClient admin, string code)
    {
        var created = await admin.PostAsJsonAsync(Branches, new CreateBranchRequest(code, code + " branch", "فرع " + code, null, null, null, null, "EGP"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static object UserBody(string username, string password, Guid roleId) => new
    {
        username,
        password,
        mustChangePassword = false,
        fullNameEn = "Staff " + username,
        fullNameAr = "موظف",
        email = $"{username}@lab.example",
        roleId,
    };

    private Task<HttpResponseMessage> PostUserAsync(HttpClient admin, string username, string password, Guid roleId) =>
        PostSealedAsync(admin, Users, "password", password, sealedValue => UserBody(username, sealedValue, roleId));

    private async Task<(string Username, string Password)> CreateUserAsync(HttpClient admin, Guid roleId)
    {
        var username = $"staff.{Suffix()}";
        var password = "a long staff password";
        var created = await PostUserAsync(admin, username, password, roleId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (username, password);
    }

    /// <summary>Seals one credential field exactly as the SPA's envelope interceptor does (D-03): the audience is the method and path.</summary>
    private static async Task<HttpResponseMessage> PostSealedAsync(HttpClient client, string path, string field, string value, Func<string, object> body)
    {
        var challenge = await new AuthClient(client).ChallengeAsync();
        return await client.PostAsJsonAsync(path, body(TestSealer.Seal(challenge, field, value, "POST " + path)));
    }

    private Task<HttpClient> SignedInClientAsync(string username) => SignedInClientAsync(username, ApiFactory.Password);

    private async Task<HttpClient> SignedInClientAsync(string username, string password)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
