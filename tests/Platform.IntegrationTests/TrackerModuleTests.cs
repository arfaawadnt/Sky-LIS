using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.Results;
using Platform.Application.Tracker;
using Platform.Domain.Identity;
using Platform.IntegrationTests.Support;
using Xunit.Abstractions;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of the Lab Tracker (LIS-CR-006, intake LIS-INT-TRK-001 §16 S-T1…S-T4): the reader returns the visits of one
/// branch registered in the window with their lines, live samples, results visible by the result rights and in all, the
/// clocks of the unsigned results and the balance figures; nothing of another branch, day or tenant; the search finds by
/// accession, sample number, patient number, name (not a VIP name without See-VIP) and mobile. Rows of the other modules are
/// written with raw SQL on their MODULES.md §5 columns, in 2003 so no other suite's rows are met.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrackerModuleTests(DatabaseFixture fixture, ITestOutputHelper output)
{
    private static readonly DateTimeOffset Day = new(2003, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task S_T1_the_reader_returns_the_visits_of_the_branch_and_day_with_their_aggregates()
    {
        var world = await WorldAsync();

        var rows = await ReadAsync(world.Tenant, Filter(world, new ResultVisibility(false, [world.GroupA], new Dictionary<Guid, IReadOnlyList<Guid>>())));

        Assert.Equal([world.VisitA, world.VisitB], rows.OrderBy(row => row.RegisteredAtUtc).Select(row => row.VisitId).ToArray());
        var a = rows.Single(row => row.VisitId == world.VisitA);
        Assert.Equal(("Nadia Samir", "نادية سمير", false, "Female", new DateOnly(1990, 1, 1)), (a.PatientName, a.PatientNameAr, a.IsVip, a.Gender, a.DateOfBirth));
        Assert.Equal(("Cash", "كاش", "Default", "افتراضي", "Desk Clerk", true), (a.PayerNameEn, a.PayerNameAr, a.TierNameEn, a.TierNameAr, a.RegisteredByName, a.Urgent));

        // lines: GLU live, CBC cancelled, the package member ignored; samples: the replaced one ignored
        Assert.Equal((1, 1), (a.LiveLines, a.CancelledLines));
        Assert.Equal((2, 1, 0, 0, 1), (a.Samples.Total, a.Samples.Ordered, a.Samples.Collected, a.Samples.Received, a.Samples.Rejected));
        Assert.Equal(("Haemolysed", "متحلل"), (a.FirstRejectionReasonEn, a.FirstRejectionReasonAr));

        // group A visible everywhere: the Ordered result; group B (Complete, panic) hidden
        Assert.Equal((1, 0, 1), (a.VisibleResults.Ordered, a.VisibleResults.Complete, a.VisibleResults.Total));
        Assert.Equal((1, 1, 2), (a.AllResults.Ordered, a.AllResults.Complete, a.AllResults.Total));
        Assert.Equal((0, 0, (string?)null), (a.VisiblePanics, a.VisibleOpenPanics, a.FirstPanicTestEn));
        Assert.Equal([Day.AddHours(10)], a.VisibleOpenClocks.Select(clock => clock.ExpectedUtc).ToArray());
        Assert.Equal([Day.AddHours(8)], a.VisibleOpenClocks.Select(clock => clock.StartUtc).ToArray());
        Assert.Equal([Day.AddHours(9), Day.AddHours(10)], a.AllOpenClocks.Select(clock => clock.ExpectedUtc).ToArray());

        var b = rows.Single(row => row.VisitId == world.VisitB);
        Assert.True(b.IsVip);
        Assert.Equal((1, 1, 2), (b.VisibleResults.Verified, b.VisibleResults.Printed, b.VisibleResults.Total));
        Assert.Empty(b.VisibleOpenClocks);
        Assert.Equal((100m, 30m, 5m, false), (b.PatientShareTotal, b.Paid, b.Refunded, b.BilledThroughClaims));   // the voided payment does not count
        Assert.Equal((0m, 0m), (a.Paid, a.Refunded));
    }

    [DatabaseFact]
    public async Task S_T2_result_rights_per_branch_decide_which_results_are_visible()
    {
        var world = await WorldAsync();

        var perBranch = new ResultVisibility(false, [], new Dictionary<Guid, IReadOnlyList<Guid>> { [world.Branch] = [world.GroupB] });
        var a = (await ReadAsync(world.Tenant, Filter(world, perBranch))).Single(row => row.VisitId == world.VisitA);
        Assert.Equal((0, 1, 1), (a.VisibleResults.Ordered, a.VisibleResults.Complete, a.VisibleResults.Total));
        Assert.Equal((1, 1, 1), (a.VisiblePanics, a.VisibleOpenPanics, a.VisibleDeltas));
        Assert.Equal(("Potassium", "بوتاسيوم"), (a.FirstPanicTestEn, a.FirstPanicTestAr));

        var elsewhere = new ResultVisibility(false, [], new Dictionary<Guid, IReadOnlyList<Guid>> { [Guid.CreateVersion7()] = [world.GroupA, world.GroupB] });
        Assert.All(await ReadAsync(world.Tenant, Filter(world, elsewhere)), row => Assert.Equal(0, row.VisibleResults.Total));

        var everything = (await ReadAsync(world.Tenant, Filter(world, ResultVisibility.Everything))).Single(row => row.VisitId == world.VisitA);
        Assert.Equal(2, everything.VisibleResults.Total);
    }

    [DatabaseFact]
    public async Task S_T3_the_search_finds_by_number_name_and_mobile_and_hides_a_vip_name_without_the_right()
    {
        var world = await WorldAsync();
        async Task<Guid[]> FindAsync(string search, bool includeVip = false, string? mobile = null) =>
            [.. (await ReadAsync(world.Tenant, Filter(world, ResultVisibility.Nothing) with { Search = search, IncludeVip = includeVip, MobileDigits = mobile })).Select(row => row.VisitId)];

        Assert.Equal([world.VisitA], await FindAsync(world.AccessionA[^5..]));
        Assert.Equal([world.VisitA], await FindAsync("smp-trk-a1"));
        Assert.Equal([world.VisitA], await FindAsync("nadia"));
        Assert.Equal([world.VisitA], await FindAsync("نادية"));
        Assert.Equal([world.VisitA], await FindAsync("1234567", mobile: "1234567"));
        Assert.Equal([world.VisitB], await FindAsync("PT-VIP"));               // the patient number finds a VIP file
        Assert.Empty(await FindAsync("karim"));                                 // A-03: not the name without See-VIP
        Assert.Equal([world.VisitB], await FindAsync("karim", includeVip: true));
        Assert.Empty(await FindAsync("nadia_"));                                // "_" is literal, not a wildcard
    }

    [DatabaseFact]
    public async Task S_T4_another_tenant_never_reads_the_branch_s_visits()
    {
        var world = await WorldAsync();

        Assert.Empty(await ReadAsync(Guid.CreateVersion7(), Filter(world, ResultVisibility.Everything)));
    }

    /// <summary>
    /// Performance target of LIS-CR-006 §0: 500 visits of a day (two lines, two samples, three results, a payment each) are
    /// read in less than 300 ms on the dev instance; the assertion leaves room for a busy test host.
    /// </summary>
    [DatabaseFact]
    public async Task S_T5_five_hundred_visits_of_a_day_are_read_quickly()
    {
        var world = await WorldAsync();
        await CloneVisitAsync(world, world.VisitA, 500);
        var filter = Filter(world, new ResultVisibility(false, [world.GroupA], new Dictionary<Guid, IReadOnlyList<Guid>>()));

        await ReadAsync(world.Tenant, filter);   // warm-up: connection pool, plan cache
        var timings = new List<long>();
        IReadOnlyList<LabTrackerRow> rows = [];
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            rows = await ReadAsync(world.Tenant, filter);
            timings.Add(watch.ElapsedMilliseconds);
        }

        output.WriteLine($"Lab Tracker read of {rows.Count} visits: {string.Join(", ", timings)} ms");
        Assert.Equal(502, rows.Count);
        Assert.True(timings.Min() < 1500, $"Read took {timings.Min()} ms.");
    }

    // ---- world --------------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid Branch, Guid GroupA, Guid GroupB, Guid VisitA, Guid VisitB, string AccessionA, Guid Cashier);

    private static LabTrackerFilter Filter(World world, ResultVisibility visibility) =>
        new(world.Branch, Day, Day.AddDays(1), null, null, false, visibility);

    private async Task<IReadOnlyList<LabTrackerRow>> ReadAsync(Guid tenant, LabTrackerFilter filter)
    {
        using var scope = fixture.AppScope(tenant);
        return await scope.ServiceProvider.GetRequiredService<ILabTrackerReader>().ReadAsync(filter, CancellationToken.None);
    }

    private async Task<World> WorldAsync()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var cashier = await RegisterUserAsync();
        var (groupA, groupB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (glucose, potassium) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await RawRows.InsertAsync("app.clinical_groups", new Dictionary<string, object?> { ["tenant_id"] = tenant, ["id"] = groupA, ["code"] = "TRKA", ["unit_code"] = "A", ["name_en"] = "Group A", ["name_ar"] = "أ" });
        await RawRows.InsertAsync("app.clinical_groups", new Dictionary<string, object?> { ["tenant_id"] = tenant, ["id"] = groupB, ["code"] = "TRKB", ["unit_code"] = "B", ["name_en"] = "Group B", ["name_ar"] = "ب" });
        await RawRows.InsertAsync("app.tests", new Dictionary<string, object?> { ["tenant_id"] = tenant, ["id"] = glucose, ["clinical_group_id"] = groupA, ["code"] = "GLU", ["name_en"] = "Glucose", ["name_ar"] = "جلوكوز" });
        await RawRows.InsertAsync("app.tests", new Dictionary<string, object?> { ["tenant_id"] = tenant, ["id"] = potassium, ["clinical_group_id"] = groupB, ["code"] = "K", ["name_en"] = "Potassium", ["name_ar"] = "بوتاسيوم" });

        var (payer, tier) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await RawRows.InsertAsync("app.payers", new Dictionary<string, object?> { ["tenant_id"] = tenant, ["id"] = payer, ["name_en"] = "Cash", ["name_ar"] = "كاش", ["search_key"] = "CASH" });
        await RawRows.InsertAsync("app.contract_tiers", new Dictionary<string, object?> { ["tenant_id"] = tenant, ["id"] = tier, ["payer_id"] = payer, ["name_en"] = "Default", ["name_ar"] = "افتراضي" });

        var (nadia, karim) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await RawRows.InsertAsync("app.patients", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = nadia, ["number"] = "PT-TRK-1", ["full_name"] = "Nadia Samir", ["name_ar"] = "نادية سمير", ["gender"] = "Female",
            ["status"] = "Active", ["date_of_birth"] = new DateOnly(1990, 1, 1), ["mobile"] = "01001234567", ["is_vip"] = false,
        });
        await RawRows.InsertAsync("app.patients", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = karim, ["number"] = "PT-VIP-2", ["full_name"] = "Karim Adel", ["gender"] = "Male",
            ["status"] = "Active", ["age_years"] = 40, ["age_recorded_on"] = new DateOnly(2003, 1, 1), ["is_vip"] = true,
        });

        // Visit A (urgent): GLU live, CBC cancelled, a package member; an Ordered sample, a Replaced one, a Rejected one; group A Ordered, group B Complete with panic and delta.
        var visitA = await InsertVisitAsync(tenant, branch, nadia, Day.AddHours(8), urgent: true, payer, tier);
        var accessionA = $"ACC-TRK-{visitA.ToString("N")[^10..].ToUpperInvariant()}";
        var live = await InsertLineAsync(tenant, visitA, "Ordered", "Service", null);
        await InsertLineAsync(tenant, visitA, "Cancelled", "Service", null, cancelledBy: cashier);
        await InsertLineAsync(tenant, visitA, "Ordered", "PackageMember", live);
        await InsertSampleAsync(tenant, visitA, "SMP-TRK-A1", "Ordered");
        await InsertSampleAsync(tenant, visitA, "SMP-TRK-A2", "Replaced");
        await InsertSampleAsync(tenant, visitA, "SMP-TRK-A3", "Rejected", ("Haemolysed", "متحلل"));
        await InsertResultAsync(tenant, visitA, branch, groupA, glucose, "Ordered", Day.AddHours(8), Day.AddHours(10));
        await InsertResultAsync(tenant, visitA, branch, groupB, potassium, "Complete", Day.AddHours(8), Day.AddHours(9), panic: true);
        await InsertResultAsync(tenant, visitA, branch, groupA, glucose, "Cancelled", Day.AddHours(8), Day.AddHours(8));

        // Visit B (VIP): signed results; 30 paid (a voided 10 does not count), 5 refunded.
        var visitB = await InsertVisitAsync(tenant, branch, karim, Day.AddHours(9), urgent: false, payer, tier);
        await InsertLineAsync(tenant, visitB, "Ordered", "Service", null);
        await InsertSampleAsync(tenant, visitB, $"SMP-TRK-B{visitB.ToString("N")[^4..]}", "Received");
        await InsertResultAsync(tenant, visitB, branch, groupA, glucose, "Verified", Day.AddHours(9), Day.AddHours(11));
        await InsertResultAsync(tenant, visitB, branch, groupA, glucose, "Printed", Day.AddHours(9), Day.AddHours(11));
        await RawRows.InsertAsync("app.visit_accounts", new Dictionary<string, object?> { ["tenant_id"] = tenant, ["id"] = visitB });
        await InsertPaymentAsync(tenant, visitB, branch, cashier, 30m, voided: false);
        await InsertPaymentAsync(tenant, visitB, branch, cashier, 10m, voided: true);
        await RawRows.InsertAsync("app.refunds", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = Guid.CreateVersion7(), ["visit_id"] = visitB, ["number"] = $"RF-{visitB.ToString("N")[^8..]}", ["amount"] = 5m,
            ["method"] = "Cash", ["cashier_user_id"] = cashier, ["branch_id"] = branch, ["refunded_at_utc"] = Day.AddHours(12),
        });

        // never read: another branch, the next day
        await InsertVisitAsync(tenant, Guid.CreateVersion7(), nadia, Day.AddHours(10), urgent: false, payer, tier);
        await InsertVisitAsync(tenant, branch, nadia, Day.AddDays(1).AddHours(1), urgent: false, payer, tier);
        return new World(tenant, branch, groupA, groupB, visitA, visitB, accessionA, cashier);
    }

    /// <summary>Copies a visit with its lines, samples and results <paramref name="copies"/> times (new ids, numbers and times through the day) with a 50.00 payment each.</summary>
    private static async Task CloneVisitAsync(World world, Guid template, int copies)
    {
        await RawRows.InsertAsync("app.visit_accounts", new Dictionary<string, object?> { ["tenant_id"] = world.Tenant, ["id"] = template });
        await InsertPaymentAsync(world.Tenant, template, world.Branch, world.Cashier, 50m, voided: false);
        await ExecuteAsync(
            """
            CREATE TEMP TABLE trk_copies ON COMMIT DROP AS
                SELECT g AS n, gen_random_uuid() AS visit_id FROM generate_series(1, @copies) AS g;
            INSERT INTO app.visits
                SELECT (jsonb_populate_record(v, jsonb_build_object('id', c.visit_id, 'accession', 'ACC-P-' || lpad(c.n::text, 6, '0'),
                        'registered_at_utc', @day + make_interval(secs => c.n * 120)))).*
                FROM app.visits v CROSS JOIN trk_copies c WHERE v.tenant_id = @tenant AND v.id = @template;
            INSERT INTO app.visit_lines
                SELECT (jsonb_populate_record(l, jsonb_build_object('id', gen_random_uuid(), 'visit_id', c.visit_id, 'parent_line_id', NULL))).*
                FROM app.visit_lines l CROSS JOIN trk_copies c WHERE l.tenant_id = @tenant AND l.visit_id = @template AND l.kind <> 'PackageMember';
            INSERT INTO app.samples
                SELECT (jsonb_populate_record(s, jsonb_build_object('id', gen_random_uuid(), 'visit_id', c.visit_id, 'tube_id', gen_random_uuid(),
                        'sample_number', 'SP-' || c.n || '-' || left(md5(s.id::text), 6)))).*
                FROM app.samples s CROSS JOIN trk_copies c WHERE s.tenant_id = @tenant AND s.visit_id = @template;
            INSERT INTO app.results
                SELECT (jsonb_populate_record(r, jsonb_build_object('id', gen_random_uuid(), 'visit_id', c.visit_id, 'line_id', gen_random_uuid()))).*
                FROM app.results r CROSS JOIN trk_copies c WHERE r.tenant_id = @tenant AND r.visit_id = @template;
            INSERT INTO app.visit_accounts
                SELECT (jsonb_populate_record(a, jsonb_build_object('id', c.visit_id))).*
                FROM app.visit_accounts a CROSS JOIN trk_copies c WHERE a.tenant_id = @tenant AND a.id = @template;
            INSERT INTO app.payments
                SELECT (jsonb_populate_record(p, jsonb_build_object('id', gen_random_uuid(), 'visit_id', c.visit_id, 'receipt_number', 'RP-' || c.n))).*
                FROM app.payments p CROSS JOIN trk_copies c WHERE p.tenant_id = @tenant AND p.visit_id = @template;
            ANALYZE app.visits; ANALYZE app.visit_lines; ANALYZE app.samples; ANALYZE app.results; ANALYZE app.payments;
            """,
            ("copies", copies),
            ("day", Day),
            ("tenant", world.Tenant),
            ("template", template));
    }

    private async Task<Guid> RegisterUserAsync()
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"trk.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Tracker cashier", "hash", Day);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private static async Task<Guid> InsertVisitAsync(Guid tenant, Guid branch, Guid patient, DateTimeOffset registeredAtUtc, bool urgent, Guid payer, Guid tier)
    {
        var id = Guid.CreateVersion7();
        await RawRows.InsertAsync("app.visits", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = id, ["accession"] = $"ACC-TRK-{id.ToString("N")[^10..].ToUpperInvariant()}", ["patient_id"] = patient,
            ["branch_id"] = branch, ["payer_id"] = payer, ["contract_tier_id"] = tier, ["status"] = "Open", ["registered_at_utc"] = registeredAtUtc,
            ["registered_by_user_id"] = Guid.CreateVersion7(), ["created_by_display_name"] = "Desk Clerk", ["urgent"] = urgent,
            ["total_list_price"] = 100m, ["discount_total"] = 0m, ["patient_share_total"] = 100m, ["payer_share_total"] = 0m,
        });
        return id;
    }

    private static async Task<Guid> InsertLineAsync(Guid tenant, Guid visit, string status, string kind, Guid? parent, Guid? cancelledBy = null)
    {
        var id = Guid.CreateVersion7();
        await RawRows.InsertAsync("app.visit_lines", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = id, ["visit_id"] = visit, ["status"] = status, ["kind"] = kind, ["parent_line_id"] = parent,
            ["cancelled_at_utc"] = status == "Cancelled" ? Day.AddHours(8) : null,
            ["cancellation_reason"] = status == "Cancelled" ? "Ordered by mistake" : null,
            ["cancelled_by_user_id"] = status == "Cancelled" ? cancelledBy : null,
        });
        return id;
    }

    private static Task InsertSampleAsync(Guid tenant, Guid visit, string number, string status, (string En, string Ar)? rejection = null) =>
        RawRows.InsertAsync("app.samples", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = Guid.CreateVersion7(), ["visit_id"] = visit, ["sample_number"] = number, ["status"] = status,
            ["tube_id"] = Guid.CreateVersion7(), ["rejection_reason_name_en"] = rejection?.En, ["rejection_reason_name_ar"] = rejection?.Ar,
            ["rejected_at_utc"] = rejection is null ? null : Day.AddHours(8).AddMinutes(30),
        });

    private static Task InsertResultAsync(Guid tenant, Guid visit, Guid branch, Guid group, Guid test, string status, DateTimeOffset startUtc, DateTimeOffset expectedUtc, bool panic = false) =>
        RawRows.InsertAsync("app.results", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = Guid.CreateVersion7(), ["visit_id"] = visit, ["line_id"] = Guid.CreateVersion7(), ["branch_id"] = branch,
            ["clinical_group_id"] = group, ["test_id"] = test, ["status"] = status, ["source"] = "Manual", ["clock_start_utc"] = startUtc,
            ["expected_at_utc"] = expectedUtc, ["is_panic"] = panic, ["delta_flag"] = panic,
        });

    private static Task InsertPaymentAsync(Guid tenant, Guid visit, Guid branch, Guid cashier, decimal amount, bool voided) =>
        RawRows.InsertAsync("app.payments", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant, ["id"] = Guid.CreateVersion7(), ["visit_id"] = visit, ["receipt_number"] = $"RC-{Guid.CreateVersion7().ToString("N")[^12..]}",
            ["amount"] = amount, ["method"] = "Cash", ["cashier_user_id"] = cashier, ["branch_id"] = branch, ["paid_at_utc"] = Day.AddHours(10),
            ["status"] = voided ? "Voided" : "Posted", ["void_reason"] = voided ? "Wrong visit" : null, ["voided_at_utc"] = voided ? Day.AddHours(11) : null,
            ["voided_by_user_id"] = voided ? cashier : null,
        });

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(value is DateTimeOffset instant
                ? new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = instant.ToUniversalTime() }
                : new NpgsqlParameter(name, value));
        }

        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
