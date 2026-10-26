using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Domain.Catalogue;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// Compatible sample conditions (LIS-CR-015 M4) on the engine: <c>app.sample_conditions.merges_into_condition_id</c> is saved
/// with its root, read by the register and the catalogue read port, and the same-tenant foreign key refuses a target that
/// is not a condition of the tenant.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CompatibleConditionsPersistenceTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_target_is_saved_and_read_by_the_register_and_the_read_port()
    {
        var tenant = Guid.CreateVersion7();
        var serum = SampleType.Create(
            "SERUM",
            "SER",
            new LocalizedText("Serum", "مصل"),
            [
                new SampleConditionSpec(null, "FAST", new LocalizedText("Fasting", "صائم"), true),
                new SampleConditionSpec(null, "RAND", new LocalizedText("Random", "عشوائي"), true),
            ],
            Now);
        var fasting = serum.Conditions.Single(condition => condition.Code == "FAST").Id;
        var random = serum.Conditions.Single(condition => condition.Code == "RAND").Id;

        using (var scope = fixture.AppScope(tenant))
        {
            scope.ServiceProvider.GetRequiredService<ISampleTypeRepository>().Add(serum);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = fixture.AppScope(tenant))
        {
            var loaded = (await scope.ServiceProvider.GetRequiredService<ISampleTypeRepository>().GetForUpdateAsync(serum.Id, CancellationToken.None))!;
            Assert.True(loaded.SetCompatibleConditions([new SampleConditionPair(random, fasting)], Now));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var view = Assert.Single(await reader.ServiceProvider.GetRequiredService<ISampleTypeRepository>().ListCompatibilityAsync(CancellationToken.None));
        Assert.Equal(fasting, view.Conditions.Single(condition => condition.Id == random).MergesIntoConditionId);
        Assert.Null(view.Conditions.Single(condition => condition.Id == fasting).MergesIntoConditionId);

        var info = Assert.Single(await reader.ServiceProvider.GetRequiredService<ICatalogueLookup>().GetSampleTypesAsync([serum.Id], CancellationToken.None));
        Assert.Equal(fasting, info.Conditions.Single(condition => condition.Id == random).MergesIntoConditionId);
        Assert.Equal(1, (await reader.ServiceProvider.GetRequiredService<AppDbContext>().Set<SampleType>().AsNoTracking().SingleAsync(row => row.Id == serum.Id)).Revision);
    }

    [DatabaseFact]
    public async Task The_foreign_key_refuses_a_target_outside_the_tenant()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var other = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [new SampleConditionSpec(null, "FAST", new LocalizedText("Fasting", "صائم"), true)], Now);
        var own = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [new SampleConditionSpec(null, "RAND", new LocalizedText("Random", "عشوائي"), true)], Now);
        foreach (var (tenant, type) in new[] { (tenantA, other), (tenantB, own) })
        {
            using var scope = fixture.AppScope(tenant);
            scope.ServiceProvider.GetRequiredService<ISampleTypeRepository>().Add(type);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("update app.sample_conditions set merges_into_condition_id = @target where tenant_id = @tenant and id = @id", connection);
        command.Parameters.AddWithValue("target", other.Conditions.Single().Id);
        command.Parameters.AddWithValue("tenant", tenantB);
        command.Parameters.AddWithValue("id", own.Conditions.Single().Id);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refused.SqlState);
    }
}
