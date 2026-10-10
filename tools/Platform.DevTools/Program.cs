using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.Organization;
using Platform.Application.Tenancy;
using Platform.Application.Treasury;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
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
//   seed-platform-admin <username> <password>                           a National Technology administrator of the platform tenant
//                                                                       (user + membership in TenantIds.Platform + role "Platform Administrator" with every permission)
// Connection string from LIS_DEVTOOLS_CONNECTION (an lis_* role on an lis* database; anything else is refused).

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: seed-user | grant-role | seed-treasury | seed-platform-admin (see the header of Program.cs)");
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

    case "seed-platform-admin" when args.Length >= 3:
    {
        var admin = await services.GetRequiredService<PlatformAdministratorSeeder>().SeedAsync(args[1], args[2], CancellationToken.None);
        Console.WriteLine($"{(admin.UserCreated ? "seeded" : "updated")} platform administrator {admin.Username} ({admin.UserId}) in tenant {TenantIds.Platform} with role {PlatformAdministratorSeeder.RoleName} ({admin.RoleId}) on {DatabaseRoleGuard.Describe(connectionString)}");
        return 0;
    }

    default:
        Console.Error.WriteLine("usage: seed-user | grant-role | seed-treasury | seed-platform-admin (see the header of Program.cs)");
        return 2;
}
