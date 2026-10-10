using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Security;

namespace Platform.ArchitectureTests.Support;

/// <summary>Builds the EF Core model exactly as production does, without a database: model building only parses the connection string.</summary>
internal static class OfflineModel
{
    public const string ConnectionString = "Host=127.0.0.1;Port=5433;Database=offline-model-only;Username=nobody";

    public static AppDbContext CreateContext(TenantContextAccessor? tenant = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContextOptions.Apply(builder, ConnectionString);
        return new AppDbContext(builder.Options, tenant ?? new TenantContextAccessor());
    }

    public static IModel Build()
    {
        // The design-time model keeps check constraints and other migration-only configuration; the runtime model drops them.
        using var context = CreateContext();
        return context.GetService<IDesignTimeModel>().Model;
    }
}
