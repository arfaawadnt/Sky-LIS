using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Pricing;
using Platform.Application.Tenancy;
using Platform.Application.Treasury;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Pricing;
using Platform.Domain.Tenancy;
using Platform.Infrastructure;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Security;
using Platform.Infrastructure.Security.Hashing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;

// Development-only seeding through the production services (same guards, same tenant stamping, same outbox).
//   seed-user  <username> <password> [tenantId]                        a platform user with one active membership
//   grant-role <username> <tenantId> <roleNameEn> <perm,perm,…>        creates or updates the role and assigns it to the membership
//   seed-treasury <tenantId> <code> <nameEn> <nameAr>                   a treasury in a generated branch
//   seed-demo-lab <username> <password> [tenantId]                      a complete Release-1 laboratory for browser walk-throughs: branch, catalogue with prices,
//                                                                       treasury, a role with every permission, users <username> and <username>.manager
//   seed-platform-admin <username> <password>                           a National Technology administrator of the platform tenant
//                                                                       (user + membership in TenantIds.Platform + role "Platform Administrator" with every permission)
// Connection string from LIS_DEVTOOLS_CONNECTION (an lis_* role on an lis* database; anything else is refused).

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: seed-user | grant-role | seed-treasury | seed-demo-lab | seed-platform-admin (see the header of Program.cs)");
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

    case "seed-treasury" when args.Length >= 5 && Guid.TryParse(args[1], out var tenant):
    {
        tenantAccessor.SetTenant(tenant);
        var treasuries = services.GetRequiredService<ITreasuryRepository>();
        var treasury = Platform.Domain.Treasury.Treasury.Create(args[2], new LocalizedText(args[3], args[4]), Guid.CreateVersion7(), clock.UtcNow);
        treasuries.Add(treasury);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Console.WriteLine($"seeded treasury {treasury.Code} ({treasury.Id}) in tenant {tenant}");
        return 0;
    }

    case "seed-demo-lab" when args.Length >= 3:
    {
        // A complete Release-1 laboratory for browser walk-throughs: one branch, two users (a laboratory administrator and a
        // shift manager who can approve the administrator's treasury access request: SoD), a role with every permission,
        // a small catalogue with prices on an active cash price list, and one treasury on the branch.
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

        var treasury = Platform.Domain.Treasury.Treasury.Create("MAIN-CASH", new LocalizedText("Main cashier", "الخزينة الرئيسية"), branch.Id, now);
        services.GetRequiredService<ITreasuryRepository>().Add(treasury);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var role = Role.Create(new LocalizedText("Laboratory Administrator", "مدير المختبر"), PermissionCatalog.Keys, now, "Every permission of the laboratory (demo seed).", SpecialRights.All);
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
        Console.WriteLine($"  branch {branch.Code} ({branch.Id}); treasury {treasury.Code} ({treasury.Id}); price list {priceList.Code}");
        Console.WriteLine($"  users {args[1]} and {managerUsername} (same password), role '{role.Name.En}' with every permission, all special and result rights");
        Console.WriteLine($"  services {string.Join(", ", tests.Select(test => test.Code))}; request treasury access as {args[1]} and approve it as {managerUsername}");
        return 0;
    }

    case "seed-platform-admin" when args.Length >= 3:
    {
        var admin = await services.GetRequiredService<PlatformAdministratorSeeder>().SeedAsync(args[1], args[2], CancellationToken.None);
        Console.WriteLine($"{(admin.UserCreated ? "seeded" : "updated")} platform administrator {admin.Username} ({admin.UserId}) in tenant {TenantIds.Platform} with role {PlatformAdministratorSeeder.RoleName} ({admin.RoleId}) on {DatabaseRoleGuard.Describe(connectionString)}");
        return 0;
    }

    default:
        Console.Error.WriteLine("usage: seed-user | grant-role | seed-treasury | seed-demo-lab | seed-platform-admin (see the header of Program.cs)");
        return 2;
}
