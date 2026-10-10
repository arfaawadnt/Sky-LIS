using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Infrastructure.Security;

namespace Platform.Infrastructure.Persistence;

/// <summary>The provider configuration shared by the runtime registration and the design-time factory.</summary>
internal static class AppDbContextOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static void Apply(DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, AppDbContext.OpsSchema))
            .UseSnakeCaseNamingConvention();
    }
}

/// <summary>
/// Lets <c>dotnet ef</c> build the model without a running database. The connection string is only parsed; set
/// <c>LIS_DESIGN_CONNECTION</c> to point the tool at a real server for <c>database update</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string ConnectionVariable = "LIS_DESIGN_CONNECTION";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? "Host=127.0.0.1;Port=5433;Database=lis;Username=lis_owner";

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContextOptions.Apply(builder, connectionString);
        return new AppDbContext(builder.Options, new TenantContextAccessor());
    }
}
