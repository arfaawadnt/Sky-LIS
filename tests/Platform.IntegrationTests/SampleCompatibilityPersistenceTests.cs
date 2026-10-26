using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// Compatibility groups of samples (LIS-CR-015 M4) on the engine: the group and its members are saved and reordered in one
/// commit, the register and the catalogue read port read them (members deactivated since are left out of planning), another
/// tenant sees nothing, the pair index treats "no condition" as one value and the foreign keys stay inside the tenant.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SampleCompatibilityPersistenceTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private sealed record Seeded(Guid GroupId, SampleType Serum, SampleType Plasma, Guid Fasting, Guid Random, Guid Fluoride);

    [DatabaseFact]
    public async Task A_group_is_saved_reordered_and_read_by_the_register_and_the_read_port()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenant);

        using (var scope = fixture.AppScope(tenant))
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISampleCompatibilityGroupRepository>();
            var loaded = (await repository.GetForUpdateAsync(seeded.GroupId, CancellationToken.None))!;
            loaded.Update(new LocalizedText("Sugar", "السكر"), [new(seeded.Serum.Id, seeded.Random), new(seeded.Serum.Id, seeded.Fasting), new(seeded.Plasma.Id, seeded.Fluoride)], [seeded.Serum, seeded.Plasma], Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var view = Assert.Single((await reader.ServiceProvider.GetRequiredService<ISampleCompatibilityGroupRepository>().ListAsync(new CatalogueAListFilter("fluor", null), PageRequest.Normalized(1, 25), CancellationToken.None)).Items);
        Assert.Equal(["RAND", "FAST", "FLU"], view.Members.Select(member => member.SampleConditionCode));
        Assert.Equal([1, 2, 3], view.Members.Select(member => member.Priority));
        Assert.All(view.Members, member => Assert.True(member.Available));

        var info = Assert.Single(await reader.ServiceProvider.GetRequiredService<ICatalogueLookup>().GetSampleTypesAsync([seeded.Serum.Id], CancellationToken.None));
        var group = Assert.Single(info.CompatibilityGroups!);
        Assert.Equal((seeded.GroupId, 3), (group.Id, group.Members.Count));
        Assert.Equal(1, group.Members.Single(member => member.SampleConditionId == seeded.Random).Priority);
    }

    [DatabaseFact]
    public async Task Members_deactivated_since_and_inactive_groups_are_left_out_of_planning()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenant);

        using (var scope = fixture.AppScope(tenant))
        {
            var plasma = (await scope.ServiceProvider.GetRequiredService<ISampleTypeRepository>().GetForUpdateAsync(seeded.Plasma.Id, CancellationToken.None))!;
            plasma.Deactivate(Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var reader = fixture.AppScope(tenant))
        {
            var info = Assert.Single(await reader.ServiceProvider.GetRequiredService<ICatalogueLookup>().GetSampleTypesAsync([seeded.Serum.Id], CancellationToken.None));
            Assert.Equal(2, Assert.Single(info.CompatibilityGroups!).Members.Count);
            var view = (await reader.ServiceProvider.GetRequiredService<ISampleCompatibilityGroupRepository>().GetViewAsync(seeded.GroupId, CancellationToken.None))!;
            Assert.False(view.Members.Single(member => member.SampleTypeId == seeded.Plasma.Id).Available);
        }

        using (var scope = fixture.AppScope(tenant))
        {
            var group = (await scope.ServiceProvider.GetRequiredService<ISampleCompatibilityGroupRepository>().GetForUpdateAsync(seeded.GroupId, CancellationToken.None))!;
            group.Deactivate(Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var after = fixture.AppScope(tenant);
        Assert.Empty(Assert.Single(await after.ServiceProvider.GetRequiredService<ICatalogueLookup>().GetSampleTypesAsync([seeded.Serum.Id], CancellationToken.None)).CompatibilityGroups!);
    }

    [DatabaseFact]
    public async Task Another_tenant_sees_nothing_and_the_active_holder_query_finds_the_other_group()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenant);

        using (var other = fixture.AppScope(Guid.CreateVersion7()))
        {
            var context = other.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await context.Set<SampleCompatibilityGroup>().IgnoreQueryFilters().CountAsync(row => row.Id == seeded.GroupId));
            Assert.Equal(0, await context.Set<SampleCompatibilityMember>().IgnoreQueryFilters().CountAsync(row => row.GroupId == seeded.GroupId));
        }

        using var scope = fixture.AppScope(tenant);
        var repository = scope.ServiceProvider.GetRequiredService<ISampleCompatibilityGroupRepository>();
        var conflict = await repository.FindActiveHolderAsync([new(seeded.Serum.Id, seeded.Random)], Guid.CreateVersion7(), CancellationToken.None);
        Assert.Equal(new CompatibilityConflict("SUGAR", "SER", "RAND"), conflict);
        Assert.Null(await repository.FindActiveHolderAsync([new(seeded.Serum.Id, seeded.Random)], seeded.GroupId, CancellationToken.None));
        Assert.Null(await repository.FindActiveHolderAsync([new(seeded.Serum.Id, null)], Guid.CreateVersion7(), CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_pair_index_counts_no_condition_once_and_the_foreign_keys_stay_in_the_tenant()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenant);
        var foreign = await SeedAsync(Guid.CreateVersion7());

        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();

        async Task<string?> InsertAsync(Guid typeId, Guid? conditionId, int priority)
        {
            await using var command = new NpgsqlCommand(
                "insert into app.sample_compatibility_members (tenant_id, id, group_id, sample_type_id, sample_condition_id, priority) values (@tenant, @id, @group, @type, @condition, @priority)",
                connection);
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("group", seeded.GroupId);
            command.Parameters.AddWithValue("type", typeId);
            command.Parameters.AddWithValue("condition", conditionId is { } value ? value : DBNull.Value);
            command.Parameters.AddWithValue("priority", priority);
            try
            {
                await command.ExecuteNonQueryAsync();
                return null;
            }
            catch (PostgresException refused)
            {
                return refused.SqlState;
            }
        }

        Assert.Null(await InsertAsync(seeded.Serum.Id, null, 10));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, await InsertAsync(seeded.Serum.Id, null, 11));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await InsertAsync(foreign.Serum.Id, null, 12));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await InsertAsync(seeded.Serum.Id, foreign.Fasting, 13));

        // the code is unique per tenant
        using var scope = fixture.AppScope(tenant);
        scope.ServiceProvider.GetRequiredService<ISampleCompatibilityGroupRepository>().Add(
            SampleCompatibilityGroup.Create("SUGAR", new LocalizedText("Again", "مرة أخرى"), [new(seeded.Serum.Id, seeded.Fasting), new(seeded.Serum.Id, seeded.Random)], [seeded.Serum], Now));
        var duplicate = await Assert.ThrowsAsync<DomainException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicate.Code);
    }

    /// <summary>Serum (FAST, RAND), Plasma (FLU) and the group SUGAR = 1 Serum·FAST, 2 Serum·RAND, 3 Plasma·FLU.</summary>
    private async Task<Seeded> SeedAsync(Guid tenant)
    {
        var serum = SampleType.Create(
            "SER",
            "Serum",
            new LocalizedText("Serum", "مصل"),
            [new SampleConditionSpec(null, "FAST", new LocalizedText("Fasting", "صائم"), true), new SampleConditionSpec(null, "RAND", new LocalizedText("Random", "عشوائي"), true)],
            Now);
        var plasma = SampleType.Create("PL", "Plasma", new LocalizedText("Plasma", "بلازما"), [new SampleConditionSpec(null, "FLU", new LocalizedText("Fluoride", "فلورايد"), true)], Now);
        var fasting = serum.Conditions.Single(condition => condition.Code == "FAST").Id;
        var random = serum.Conditions.Single(condition => condition.Code == "RAND").Id;
        var fluoride = plasma.Conditions.Single().Id;

        using (var scope = fixture.AppScope(tenant))
        {
            scope.ServiceProvider.GetRequiredService<ISampleTypeRepository>().Add(serum);
            scope.ServiceProvider.GetRequiredService<ISampleTypeRepository>().Add(plasma);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var group = SampleCompatibilityGroup.Create("SUGAR", new LocalizedText("Sugar samples", "عينات السكر"), [new(serum.Id, fasting), new(serum.Id, random), new(plasma.Id, fluoride)], [serum, plasma], Now);
        using (var scope = fixture.AppScope(tenant))
        {
            scope.ServiceProvider.GetRequiredService<ISampleCompatibilityGroupRepository>().Add(group);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        return new Seeded(group.Id, serum, plasma, fasting, random, fluoride);
    }
}
