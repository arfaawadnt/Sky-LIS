using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Pricing;
using Platform.Application.PlatformAdmin;
using Platform.Application.Tenancy;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Pricing;
using Platform.Domain.Tenancy;
using Platform.Infrastructure;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Security;
using Platform.Infrastructure.Security.Hashing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

// Development-only seeding through the production services (same guards, same tenant stamping, same outbox).
//   seed-user  <username> <password> [tenantId]                        a platform user with one active membership
//   grant-role <username> <tenantId> <roleNameEn> <perm,perm,…>        creates or updates the role and assigns it to the membership
//   seed-demo-lab <username> <password> [tenantId]                      a complete Release-1 laboratory for browser walk-throughs: branch, catalogue with prices,
//                                                                       reasons, a role with every laboratory permission, users <username> and <username>.manager
//   seed-reasons <tenantId>                                             cancellation, refund, discount and rejection reasons (idempotent by name)
//   seed-platform-admin <username> <password>                           a National Technology administrator of the platform tenant
//                                                                       (user + membership in TenantIds.Platform + role "Platform Administrator" with every permission)
//   register-tenant-client <tenantId> <slug> <nameEn> <nameAr>          LIS-CR-005: the client record (saas.tenants) of an existing tenant that has none, so its
//                                                                       users can sign in at <base>/lab/<slug>; refused when one exists or the slug is taken.
//                                                                       Country/governorate/plan: the first active country (by English name) with an active
//                                                                       governorate, the subscription default plan (else any plan); address, manager and mobiles
//                                                                       are placeholders (ClientRecordRegistrar), corrected later on SA-04
// seed-demo-lab also registers the client record of its tenant (slug demo-lab, else the suggestion rule), when one is missing.
// Connection string from LIS_DEVTOOLS_CONNECTION (an lis_* role on an lis* database; anything else is refused).

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: seed-user | grant-role | seed-demo-lab | seed-reasons | seed-platform-admin | register-tenant-client (see the header of Program.cs)");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("LIS_DEVTOOLS_CONNECTION")
    ?? throw new InvalidOperationException("Set LIS_DEVTOOLS_CONNECTION to an LIS connection string (lis_app on database lis).");

var targetViolations = DatabaseRoleGuard.InspectConnectionTarget(connectionString);
if (targetViolations.Count > 0)
{
    Console.Error.WriteLine("refused: " + string.Join("; ", targetViolations));
    return 3;
}

await using var provider = new ServiceCollection()
    .AddSingleton<IPasswordHasher, Argon2idHasher>()
    .AddInfrastructure(connectionString)
    .AddScoped<PlatformAdministratorSeeder>()
    .AddScoped<ClientRecordRegistrar>()
    .BuildServiceProvider();
using var scope = provider.CreateScope();
var services = scope.ServiceProvider;
var clock = services.GetRequiredService<IClock>();
var unitOfWork = services.GetRequiredService<IUnitOfWork>();
var tenantAccessor = services.GetRequiredService<TenantContextAccessor>();

switch (args[0])
{
    case "seed-user" when args.Length >= 3:
    {
        var tenantId = args.Length > 3 && Guid.TryParse(args[3], out var parsed) ? parsed : Guid.CreateVersion7();
        tenantAccessor.SetTenant(tenantId);
        var users = services.GetRequiredService<IPlatformUserRepository>();
        if (await users.UsernameExistsAsync(args[1], CancellationToken.None))
        {
            Console.Error.WriteLine($"user '{args[1]}' already exists");
            return 4;
        }

        var user = PlatformUser.Register(args[1], $"{args[1]}@lis.local", "Development User", new Argon2idHasher().Hash(args[2]), clock.UtcNow);
        users.Add(user);
        services.GetRequiredService<ITenantMembershipRepository>()
            .Add(TenantMembership.Create(tenantId, user.Id, $"DEV-{user.Username[..Math.Min(6, user.Username.Length)].ToUpperInvariant()}", new LocalizedText("Development User", "مستخدم التطوير"), "en", clock.UtcNow));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Console.WriteLine($"seeded user {user.Username} ({user.Id}) with a membership in tenant {tenantId} on {DatabaseRoleGuard.Describe(connectionString)}");
        return 0;
    }

    case "grant-role" when args.Length >= 5 && Guid.TryParse(args[2], out var tenant):
    {
        tenantAccessor.SetTenant(tenant);
        var user = await services.GetRequiredService<IPlatformUserRepository>().FindByUsernameAsync(args[1], CancellationToken.None);
        if (user is null)
        {
            Console.Error.WriteLine($"user '{args[1]}' not found");
            return 4;
        }

        var memberships = services.GetRequiredService<ITenantMembershipRepository>();
        var membership = await memberships.FindByPlatformUserAsync(user.Id, CancellationToken.None);
        if (membership is null)
        {
            Console.Error.WriteLine($"user '{args[1]}' has no membership in tenant {tenant}");
            return 4;
        }

        var keys = args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var roles = services.GetRequiredService<IRoleRepository>();
        var role = await roles.FindByNameAsync(args[3], CancellationToken.None);
        if (role is null)
        {
            role = Role.Create(new LocalizedText(args[3], args[3]), keys, clock.UtcNow);
            roles.Add(role);
        }
        else
        {
            role.SetPermissions(keys, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);   // the role needs its tenant stamp before the membership references it
        membership.AssignRole(role, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Console.WriteLine($"role '{args[3]}' [{string.Join(", ", role.PermissionKeys)}] assigned to {user.Username} in tenant {tenant}");
        return 0;
    }

    case "seed-demo-lab" when args.Length >= 3:
    {
        // A complete Release-1 laboratory for browser walk-throughs: one branch, two users (a laboratory administrator and a
        // shift manager, for the flows with segregation of duties), a role with every permission, a small catalogue with
        // prices on an active cash price list, and the reasons. Money is taken by the session user on the session branch
        // (LIS-CR-004, 2026-10-20: no treasury).
        var tenantId = args.Length > 3 && Guid.TryParse(args[3], out var parsedTenant) ? parsedTenant : Guid.CreateVersion7();
        tenantAccessor.SetTenant(tenantId);
        var now = clock.UtcNow;
        var hasher = new Argon2idHasher();
        var users = services.GetRequiredService<IPlatformUserRepository>();
        var managerUsername = $"{args[1]}.manager";
        foreach (var username in new[] { args[1], managerUsername })
        {
            if (await users.UsernameExistsAsync(username, CancellationToken.None))
            {
                Console.Error.WriteLine($"user '{username}' already exists");
                return 4;
            }
        }

        var branch = Branch.Create("MAIN", new LocalizedText("Main Branch", "الفرع الرئيسي"), null, null, null, null, "EGP", now);
        services.GetRequiredService<IBranchRepository>().Add(branch);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var chemistry = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Clinical Chemistry", "الكيمياء الإكلينيكية"), "Clinical chemistry", null, 1, now);
        var haematology = ClinicalGroup.Create("HEMA", "H", new LocalizedText("Haematology", "أمراض الدم"), "Haematology", null, 2, now);
        var serum = SampleType.Create("SER", "Serum", new LocalizedText("Serum", "مصل"), [], now);
        var wholeBlood = SampleType.Create("EDTA", "EDTA", new LocalizedText("Whole blood (EDTA)", "دم كامل (EDTA)"), [], now);
        var mmol = MeasurementUnit.Create("mmol/L", now);
        var mgdl = MeasurementUnit.Create("mg/dL", now);
        var gdl = MeasurementUnit.Create("g/dL", now);
        var groups = services.GetRequiredService<IClinicalGroupRepository>();
        groups.Add(chemistry);
        groups.Add(haematology);
        var sampleTypes = services.GetRequiredService<ISampleTypeRepository>();
        sampleTypes.Add(serum);
        sampleTypes.Add(wholeBlood);
        var units = services.GetRequiredService<IMeasurementUnitRepository>();
        units.Add(mmol);
        units.Add(mgdl);
        units.Add(gdl);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        LabTest NumericTest(string code, string nameEn, string nameAr, ClinicalGroup group, SampleType sampleType, MeasurementUnit unit, int decimals, decimal low, decimal high, decimal panicLow, decimal panicHigh)
        {
            var test = LabTest.Create(code, new LabTestDefinition(new LocalizedText(nameEn, nameAr), nameEn, null, null, group.Id, LabTestType.Numeric, sampleType.Id, null, unit.Id, GenderRestriction.Any, null, decimals, false, true, 1, null), now);
            test.ReplaceRangeBands([new RangeBandSpec(GenderRestriction.Any, 0, 150, RangeAgeUnit.Years, null, low, high, panicLow, panicHigh, null, null, null)], now);
            return test;
        }

        var tests = new[]
        {
            NumericTest("K", "Potassium", "بوتاسيوم", chemistry, serum, mmol, 1, 3.5m, 5.0m, 2.5m, 6.5m),
            NumericTest("NA", "Sodium", "صوديوم", chemistry, serum, mmol, 0, 135m, 145m, 120m, 160m),
            NumericTest("GLU", "Glucose (fasting)", "جلوكوز صائم", chemistry, serum, mgdl, 0, 70m, 110m, 40m, 400m),
            NumericTest("HGB", "Haemoglobin", "هيموجلوبين", haematology, wholeBlood, gdl, 1, 12.0m, 16.0m, 7.0m, 20.0m),
        };
        var labTests = services.GetRequiredService<ILabTestRepository>();
        foreach (var test in tests)
        {
            labTests.Add(test);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var catalogue = services.GetRequiredService<IServiceRepository>();
        var prices = new List<PriceLineInput>();
        var sequence = 10;
        var shortcut = 1;
        foreach (var (test, price) in tests.Zip(new[] { 120m, 120m, 90m, 150m }))
        {
            var group = test.ClinicalGroupId == chemistry.Id ? chemistry : haematology;
            var sampleType = test.SampleTypeId == serum.Id ? serum : wholeBlood;
            var service = Service.Create(
                test.Code,
                ServiceType.Test,
                new ServiceDefinition(test.Code, shortcut++, test.Name, test.ReportName, null, null, group.Id, sampleType.Id, null, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null),
                sequence,
                [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)],
                now);
            sequence += 10;
            catalogue.Add(service);
            prices.Add(new PriceLineInput(service.Id, price, true));
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var priceList = BasePriceList.Create($"CASH-{now.Year}", new LocalizedText($"Cash prices {now.Year}", $"أسعار النقدي {now.Year}"), now.Year, now);
        priceList.UpsertLines(prices, now);
        priceList.Activate(now);
        services.GetRequiredService<IBasePriceListRepository>().Add(priceList);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        var reasonsSeeded = await SeedReasonsAsync(services, unitOfWork, now);

        // every key a laboratory may grant, never the platform-only modules (validation F-07: ORG-033 refused every later save of the role)
        var role = Role.Create(new LocalizedText("Laboratory Administrator", "مدير المختبر"), EffectivePrivileges.TenantGrantableKeys, now, "Every permission of the laboratory (demo seed).", SpecialRights.All);
        role.SetResultRights([new ResultRight(chemistry.Id, null, true, true, true, true, true), new ResultRight(haematology.Id, null, true, true, true, true, true)], now);
        services.GetRequiredService<IRoleRepository>().Add(role);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);   // the role needs its tenant stamp before the memberships reference it

        var memberships = services.GetRequiredService<ITenantMembershipRepository>();
        foreach (var (username, displayEn, displayAr, code) in new[]
        {
            (args[1], "Laboratory Administrator", "مدير المختبر", "LAB-ADMIN"),
            (managerUsername, "Shift Manager", "مشرف الوردية", "LAB-MGR"),
        })
        {
            var user = PlatformUser.Register(username, $"{username}@lis.local", displayEn, hasher.Hash(args[2]), now);
            users.Add(user);
            var membership = TenantMembership.Create(tenantId, user.Id, code, new LocalizedText(displayEn, displayAr), "en", now);
            membership.AssignBranches([branch.Id]);
            membership.AssignRole(role, now);
            memberships.Add(membership);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Console.WriteLine($"seeded demo laboratory in tenant {tenantId} on {DatabaseRoleGuard.Describe(connectionString)}");
        Console.WriteLine($"  branch {branch.Code} ({branch.Id}); price list {priceList.Code}");
        Console.WriteLine($"  users {args[1]} and {managerUsername} (same password), role '{role.Name.En}' with every permission, all special and result rights");
        Console.WriteLine($"  services {string.Join(", ", tests.Select(test => test.Code))}; {reasonsSeeded} reasons");

        // LIS-CR-005: sign-in is by laboratory address, so the demo laboratory needs a client record (and with it a slug).
        var registered = await RegisterClientRecordAsync(services, tenantId, "demo-lab", "Demo Laboratory", "مختبر تجريبي", suggestWhenTaken: true);
        if (registered is null)
        {
            return 5;
        }

        Console.WriteLine($"  client record: address /lab/{registered.PortalSlug} (placeholders for address, manager and mobiles; correct them on SA-04)");
        return 0;
    }

    case "register-tenant-client" when args.Length >= 5 && Guid.TryParse(args[1], out var clientTenant):
    {
        var registered = await RegisterClientRecordAsync(services, clientTenant, args[2], args[3], args[4], suggestWhenTaken: false);
        if (registered is null)
        {
            return 5;
        }

        Console.WriteLine($"registered client record of tenant {clientTenant}: '{registered.Name.En}' at /lab/{registered.PortalSlug}, status {registered.Status}, country {registered.CountryId}, plan {registered.PlanId} on {DatabaseRoleGuard.Describe(connectionString)}");
        Console.WriteLine($"  placeholders: address and manager name '{ClientRecordRegistrar.PlaceholderText}', mobiles {registered.Mobile}; correct them on SA-04");
        return 0;
    }

    case "seed-reasons" when args.Length >= 2 && Guid.TryParse(args[1], out var reasonsTenant):
    {
        // Cancellation, refund, discount and rejection reasons a laboratory needs before the money and sample flows run
        // (a provisioned laboratory receives them from the platform catalogue; a hand-seeded one has none). Idempotent by name.
        tenantAccessor.SetTenant(reasonsTenant);
        var count = await SeedReasonsAsync(services, unitOfWork, clock.UtcNow);
        Console.WriteLine($"seeded {count} reasons in tenant {reasonsTenant} on {DatabaseRoleGuard.Describe(connectionString)}");
        return 0;
    }

    case "seed-platform-admin" when args.Length >= 3:
    {
        var admin = await services.GetRequiredService<PlatformAdministratorSeeder>().SeedAsync(args[1], args[2], CancellationToken.None);
        Console.WriteLine($"{(admin.UserCreated ? "seeded" : "updated")} platform administrator {admin.Username} ({admin.UserId}) in tenant {TenantIds.Platform} with role {PlatformAdministratorSeeder.RoleName} ({admin.RoleId}) on {DatabaseRoleGuard.Describe(connectionString)}");
        return 0;
    }

    default:
        Console.Error.WriteLine("usage: seed-user | grant-role | seed-demo-lab | seed-reasons | seed-platform-admin | register-tenant-client (see the header of Program.cs)");
        return 2;
}

// LIS-CR-005: the client record of an existing tenant. Reference data: the first active country (by English name) with an active
// governorate, that governorate (first by English name), the subscription default plan, else any plan. Null (with a message) when
// the tenant already has a record, the slug is taken or invalid, or no country, governorate or plan exists yet.
static async Task<Tenant?> RegisterClientRecordAsync(IServiceProvider services, Guid tenantId, string slug, string nameEn, string nameAr, bool suggestWhenTaken)
{
    var context = services.GetRequiredService<AppDbContext>();
    if (await context.Set<Tenant>().AsNoTracking().AnyAsync(tenant => tenant.Id == tenantId))
    {
        var existing = await context.Set<Tenant>().AsNoTracking().Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.PortalSlug).SingleAsync();
        Console.Error.WriteLine($"tenant {tenantId} already has a client record (address /lab/{existing})");
        return suggestWhenTaken ? await context.Set<Tenant>().AsNoTracking().SingleAsync(tenant => tenant.Id == tenantId) : null;
    }

    var place = await (
        from country in context.Set<Country>().AsNoTracking()
        join governorate in context.Set<Governorate>().AsNoTracking() on country.Id equals governorate.CountryId
        where country.IsActive && governorate.IsActive
        orderby country.Name.En, governorate.Name.En
        select new { CountryId = country.Id, GovernorateId = governorate.Id }).FirstOrDefaultAsync();
    var plan = await services.GetRequiredService<IPlanRepository>().FindDefaultForSubscriptionAsync(CancellationToken.None)
        ?? await context.Set<Plan>().AsNoTracking().OrderBy(candidate => candidate.Code).FirstOrDefaultAsync();
    if (place is null || plan is null)
    {
        Console.Error.WriteLine("refused: no active country with an active governorate, or no plan; create them in the Super Admin portal first (SA-10, SA-11, SA-05)");
        return null;
    }

    try
    {
        return await services.GetRequiredService<ClientRecordRegistrar>().RegisterAsync(tenantId, slug, nameEn, nameAr, place.CountryId, place.GovernorateId, plan.Id, suggestWhenTaken, CancellationToken.None);
    }
    catch (DomainException exception)
    {
        Console.Error.WriteLine($"refused: {exception.Code} {exception.Message}");
        return null;
    }
}

static async Task<int> SeedReasonsAsync(IServiceProvider services, IUnitOfWork unitOfWork, DateTimeOffset now)
{
    var reasons = services.GetRequiredService<IReasonRepository>();
    var wanted = new (ReasonKind Kind, string En, string Ar)[]
    {
        (ReasonKind.Cancellation, "Patient declined", "رفض المريض"),
        (ReasonKind.Cancellation, "Ordered by mistake", "طُلب بالخطأ"),
        (ReasonKind.Refund, "Service cancelled", "إلغاء خدمة"),
        (ReasonKind.Refund, "Overpayment", "دفع زائد"),
        (ReasonKind.Discount, "Staff and relatives", "العاملون وذووهم"),
        (ReasonKind.Discount, "Management approval", "موافقة الإدارة"),
        (ReasonKind.Rejection, "Haemolysed sample", "عينة متحللة"),
        (ReasonKind.Rejection, "Insufficient volume", "كمية غير كافية"),
    };
    var added = 0;
    foreach (var (kind, en, ar) in wanted)
    {
        if (await reasons.NameExistsAsync(kind, en, null, CancellationToken.None))
        {
            continue;
        }

        reasons.Add(Reason.Create(kind, new LocalizedText(en, ar), now));
        added++;
    }

    if (added > 0)
    {
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    return added;
}
