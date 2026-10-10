using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;

namespace Platform.IntegrationTests;

/// <summary>01 §4.6: the newest migration round-trips on a real PostgreSQL 17 (down to nothing, up again) and the model has no pending changes.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MigrationRoundTripTests
{
    [DatabaseFact]
    public async Task The_migrations_apply_revert_and_reapply_and_the_model_matches_the_snapshot()
    {
        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var scope = owner.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("0");
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync());

        await migrator.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges(), "the model differs from the last migration snapshot; add a migration");

        // Every table the model maps exists after the migrations, and the migrations create nothing the model does not know.
        var tables = await context.Database.SqlQueryRaw<string>("SELECT table_schema || '.' || table_name AS \"Value\" FROM information_schema.tables WHERE table_schema IN ('identity','app','ops','audit','read','saas') AND table_type = 'BASE TABLE' AND table_name NOT LIKE '\\_\\_%' ORDER BY 1").ToListAsync();
        var modelTables = context.Model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null)
            .Select(entityType => $"{entityType.GetSchema() ?? "public"}.{entityType.GetTableName()}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(modelTables, tables);

        await DatabaseFixture.TruncateAsync(context);
    }
}
