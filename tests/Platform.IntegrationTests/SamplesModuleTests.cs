using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Application.Samples;
using Platform.Application.Samples.Policies;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.Registration;
using Platform.Domain.Samples;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-SMP-001: the policy end to end against PostgreSQL, the unique indexes of
/// INV-SMP-001/002, tenant isolation of both tables, the projections with their timeline, and the read port Results uses.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SamplesModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_policy_creates_received_samples_once_with_the_Release_1_defaults_and_the_projections_read_them()
    {
        var tenant = Guid.CreateVersion7();
        var registrar = await RegisterUserAsync("Reception Clerk");
        var registered = Registered(registrar, "SMP-2026-01001", "SMP-2026-01002");

        await HandleAsync(tenant, registered);
        await HandleAsync(tenant, registered);   // the processor retries: idempotent on TubeId

        using var scope = fixture.AppScope(tenant, registrar);
        var samples = scope.ServiceProvider.GetRequiredService<ISampleRepository>();
        var page = await samples.ListAsync(new SampleFilter(registered.VisitId, null, null, null, null, null, null, null), PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, item => Assert.Equal(("Received", registered.Accession, registered.BranchId), (item.Status, item.Accession, item.BranchId)));

        var byAccession = await samples.ListAsync(new SampleFilter(null, registered.Accession.ToLowerInvariant(), SampleStatus.Received, Now.AddHours(-1), Now.AddHours(1), registered.BranchId, null, null), PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal(2, byAccession.TotalCount);
        var bySearch = await samples.ListAsync(new SampleFilter(null, null, null, null, null, null, null, "SMP-2026-0100"), PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal(2, bySearch.TotalCount);

        var id = page.Items.Single(item => item.SampleNumber == "SMP-2026-01001").Id;
        var detail = (await samples.GetDetailAsync(id, CancellationToken.None))!;
        Assert.True(detail.IsResultEntryAllowed);
        Assert.Equal(("Reception Clerk", registered.BranchId), (detail.Collected!.UserName, detail.Collected.BranchId!.Value));
        Assert.Equal(registered.Tubes[0].ServiceIds, detail.Services.Select(service => service.Id));

        var steps = await samples.GetTimelineStepsAsync(id, CancellationToken.None);
        Assert.Equal(["Ordered", "Collected", "Received"], steps.Select(step => step.Action));
        Assert.Equal([1, 2, 3], steps.Select(step => step.Sequence));
        Assert.All(steps, step => Assert.Equal("Reception Clerk", step.UserName));

        var label = (await samples.GetLabelAsync(id, 2, CancellationToken.None))!;
        Assert.Equal(("SMP-2026-01001", registered.Accession, 2, Now), (label.SampleNumber, label.Accession, label.Copies, label.LabelAtUtc));
        Assert.Equal(2, (await samples.GetVisitLabelsAsync(registered.VisitId, 1, CancellationToken.None)).Count);
    }

    [DatabaseFact]
    public async Task Sample_numbers_are_unique_per_tenant_and_a_tube_has_one_sample_per_generation()
    {
        var tenant = Guid.CreateVersion7();
        var tube = Guid.CreateVersion7();
        await AddAsync(tenant, NewSample(tube, "SMP-2026-02001"));

        var sameNumber = await Assert.ThrowsAsync<DomainException>(() => AddAsync(tenant, NewSample(Guid.CreateVersion7(), "SMP-2026-02001")));
        var sameTube = await Assert.ThrowsAsync<DomainException>(() => AddAsync(tenant, NewSample(tube, "SMP-2026-02002")));
        Assert.Equal(ProblemCodes.UniqueViolation, sameNumber.Code);
        Assert.Equal(ProblemCodes.UniqueViolation, sameTube.Code);

        await AddAsync(Guid.CreateVersion7(), NewSample(Guid.CreateVersion7(), "SMP-2026-02001"));   // another tenant may reuse the text
    }

    [DatabaseFact]
    public async Task Samples_and_their_timelines_of_another_tenant_are_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var sample = NewSample(Guid.CreateVersion7(), "SMP-2026-03001");
        await AddAsync(tenantA, sample);

        using var scopeB = fixture.AppScope(tenantB);
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<Sample>().IgnoreQueryFilters().CountAsync(row => row.Id == sample.Id));
        Assert.Equal(0, await context.Set<SampleEvent>().IgnoreQueryFilters().CountAsync(row => row.SampleId == sample.Id));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<ISampleRepository>().GetForUpdateAsync(sample.Id, CancellationToken.None));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<ISampleStatusLookup>().BySampleIdAsync(sample.Id, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task A_replacement_takes_over_the_tube_and_the_status_lookup_follows_it()
    {
        var tenant = Guid.CreateVersion7();
        var technician = await RegisterUserAsync("Bench Technician");
        var branch = Guid.CreateVersion7();
        var original = NewSample(Guid.CreateVersion7(), "SMP-2026-04001");
        original.Receive(technician, branch, Now);
        await AddAsync(tenant, original);

        using (var scope = fixture.AppScope(tenant, technician, branchId: branch))
        {
            var samples = scope.ServiceProvider.GetRequiredService<ISampleRepository>();
            var tracked = (await samples.GetByNumberForUpdateAsync("SMP-2026-04001", CancellationToken.None))!;
            Assert.Equal(3, tracked.Events.Count);
            tracked.Reject(new RejectionReason(Guid.CreateVersion7(), "Hemolysis", "تحلل الدم"), "clotted", technician, branch, Now.AddMinutes(10));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = fixture.AppScope(tenant, technician, branchId: branch))
        {
            var lookup = scope.ServiceProvider.GetRequiredService<ISampleStatusLookup>();
            Assert.False(await lookup.IsResultEntryAllowedAsync(original.TubeId, CancellationToken.None));
            var rejected = await scope.ServiceProvider.GetRequiredService<ISampleRepository>().ListRejectedAsync(Now, Now.AddDays(1), null, CancellationToken.None);
            Assert.Equal(("SMP-2026-04001", "Hemolysis", "clotted"), (Assert.Single(rejected).SampleNumber, rejected[0].RejectionReasonNameEn, rejected[0].RejectionNote));
        }

        Guid replacementId;
        using (var scope = fixture.AppScope(tenant, technician, branchId: branch))
        {
            var samples = scope.ServiceProvider.GetRequiredService<ISampleRepository>();
            var tracked = (await samples.GetForUpdateAsync(original.Id, CancellationToken.None))!;
            var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Sample.NumberKind, Sample.NumberPrefix, Now, CancellationToken.None);
            var replacement = tracked.Replace(number, "recollected", technician, branch, Now.AddMinutes(20));
            replacement.AutoAdvance(true, true, technician, branch, Now.AddMinutes(20));
            samples.Add(replacement);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            replacementId = replacement.Id;
        }

        using var reader = fixture.AppScope(tenant, technician);
        var status = (await reader.ServiceProvider.GetRequiredService<ISampleStatusLookup>().ByTubeAsync(original.TubeId, CancellationToken.None))!;
        Assert.Equal((replacementId, "Received", true), (status.SampleId, status.Status, status.IsResultEntryAllowed));
        Assert.StartsWith("SMP-2026-", status.SampleNumber, StringComparison.Ordinal);
        var byTubes = await reader.ServiceProvider.GetRequiredService<ISampleStatusLookup>().ByTubesAsync([original.TubeId, Guid.CreateVersion7()], CancellationToken.None);
        Assert.Equal(replacementId, Assert.Single(byTubes).Value.SampleId);

        var repository = reader.ServiceProvider.GetRequiredService<ISampleRepository>();
        var old = (await repository.GetDetailAsync(original.Id, CancellationToken.None))!;
        Assert.Equal(("Replaced", replacementId), (old.Summary.Status, old.ReplacedBy!.SampleId));
        Assert.Equal(("Bench Technician", "Hemolysis"), (old.Rejection!.Step.UserName, old.Rejection.ReasonNameEn));
        var steps = await repository.GetTimelineStepsAsync(original.Id, CancellationToken.None);
        Assert.Equal(["Ordered", "Collected", "Received", "Rejected", "Replaced"], steps.Select(step => step.Action));
        Assert.Equal(("Hemolysis", "clotted"), (steps.Single(step => step.Action == "Rejected").ReasonNameEn, steps.Single(step => step.Action == "Rejected").Note));   // the snapshot when the catalogue has no such reason
        Assert.Equal(original.Id, (await repository.GetDetailAsync(replacementId, CancellationToken.None))!.Replaces!.SampleId);
    }

    [DatabaseFact]
    public async Task The_cancellation_policy_keeps_the_sample_while_the_visit_lines_table_reports_live_lines_or_is_absent()
    {
        var tenant = Guid.CreateVersion7();
        var sample = NewSample(Guid.CreateVersion7(), "SMP-2026-05001");
        await AddAsync(tenant, sample);

        using var scope = fixture.AppScope(tenant);
        var lines = scope.ServiceProvider.GetRequiredService<IVisitLineLookup>();
        var otherLine = Guid.CreateVersion7();
        var hasLive = await lines.TubeHasLiveLinesAsync(sample.TubeId, otherLine, CancellationToken.None);

        // Without visit lines for the tube the lookup answers "none live" once Registration's table exists; while it does not, it answers "live" (keep).
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installed = (await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'app' AND table_name = 'visit_lines'").ToListAsync())[0] == 1;
        Assert.Equal(!installed, hasLive);
    }

    private static VisitRegistered Registered(Guid registrar, params string[] numbers) =>
        new(
            Guid.CreateVersion7(),
            $"ACC-2026-{Guid.CreateVersion7().ToString("N")[..5].ToUpperInvariant()}",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            null,
            false,
            registrar,
            [.. numbers.Select(number => new RegisteredTube(Guid.CreateVersion7(), number, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [Guid.CreateVersion7(), Guid.CreateVersion7()]))],
            [],
            Now);

    private static Sample NewSample(Guid tubeId, string number) =>
        Sample.Create(tubeId, number, Guid.CreateVersion7(), "ACC-2026-00001", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, [Guid.CreateVersion7()], null, Now);

    private async Task HandleAsync(Guid tenantId, VisitRegistered registered)
    {
        using var scope = fixture.AppScope(tenantId);
        var services = scope.ServiceProvider;
        var policy = new CreateSamplesOnVisitRegisteredPolicy(services.GetRequiredService<ISampleRepository>(), services.GetRequiredService<ITenantSettings>(), services.GetRequiredService<IUnitOfWork>());
        await policy.HandleAsync(registered, CancellationToken.None);
    }

    private async Task AddAsync(Guid tenantId, Sample sample)
    {
        using var scope = fixture.AppScope(tenantId);
        scope.ServiceProvider.GetRequiredService<ISampleRepository>().Add(sample);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> RegisterUserAsync(string displayName)
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"smp.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }
}
