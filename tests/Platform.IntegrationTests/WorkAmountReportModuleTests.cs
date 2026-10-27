using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Reporting;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of the work amount report (change request 2026-10-13 item 3; intake LIS-INT-REP-001 §16 S-W9…S-W11):
/// the reader returns the live top-level lines of one branch's visits registered in the window and nothing of another
/// branch, another tenant, a cancelled line or a package member; the aggregator then groups them by payer and by day.
/// Registration rows are written with raw SQL on the MODULES.md §5 columns, in 2002 so no other suite's rows are met.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class WorkAmountReportModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Day = new(2002, 5, 10, 0, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task S_W9_the_reader_returns_the_live_lines_of_the_branch_in_the_window_only()
    {
        var world = await WorldAsync();

        using var scope = fixture.AppScope(world.Tenant);
        var lines = await scope.ServiceProvider.GetRequiredService<IWorkAmountReader>().ReadAsync(world.Branch, Day, Day.AddDays(2), CancellationToken.None);

        // V1: CBC (cancelled GLU left out), V2: the package (member left out), V3 on the next day; V4 (all cancelled), the other branch and the day after the window never appear
        Assert.Equal(["CBC", "PKG", "TSH"], lines.Select(line => line.ServiceCode).ToArray());
        Assert.Equal([world.VisitA, world.VisitB, world.VisitC], lines.Select(line => line.VisitId).ToArray());
        Assert.Equal((100m, 80m, 20m, 0m), (lines[0].ListPrice, lines[0].PatientShare, lines[0].PayerShare, lines[0].Discount));
        Assert.Equal((200m, 190m, 0m, 10m), (lines[1].ListPrice, lines[1].PatientShare, lines[1].PayerShare, lines[1].Discount));
        Assert.Equal(world.Doctor, lines[0].ReferringDoctorId);
        Assert.Null(lines[1].ReferringDoctorId);

        // the window's end is exclusive: the first day alone holds V1 and V2
        var firstDay = await scope.ServiceProvider.GetRequiredService<IWorkAmountReader>().ReadAsync(world.Branch, Day, Day.AddDays(1), CancellationToken.None);
        Assert.Equal([world.VisitA, world.VisitB], firstDay.Select(line => line.VisitId).ToArray());
    }

    [DatabaseFact]
    public async Task S_W10_another_tenant_never_sees_the_branch_s_lines()
    {
        var world = await WorldAsync();

        using var other = fixture.AppScope(Guid.CreateVersion7());
        var lines = await other.ServiceProvider.GetRequiredService<IWorkAmountReader>().ReadAsync(world.Branch, Day, Day.AddDays(2), CancellationToken.None);

        Assert.Empty(lines);

        // the other tenant's own visit in a branch of the same id is its own
        using var own = fixture.AppScope(world.OtherTenant);
        var theirs = await own.ServiceProvider.GetRequiredService<IWorkAmountReader>().ReadAsync(world.Branch, Day, Day.AddDays(2), CancellationToken.None);
        Assert.Equal(["OTH"], theirs.Select(line => line.ServiceCode).ToArray());
    }

    [DatabaseFact]
    public async Task S_W11_the_lines_fold_into_payer_and_day_rows_with_a_grand_total()
    {
        var world = await WorldAsync();
        using var scope = fixture.AppScope(world.Tenant);
        var lines = await scope.ServiceProvider.GetRequiredService<IWorkAmountReader>().ReadAsync(world.Branch, Day, Day.AddDays(2), CancellationToken.None);

        var byPayer = WorkAmountAggregator.Rows(lines, line => line.PayerId.ToString(), key => new WorkAmountGroupLabel(key == world.PayerCash.ToString() ? "Cash" : "Insurer", key));
        Assert.Equal(["Cash", "Insurer"], byPayer.Select(row => row.LabelEn).ToArray());
        Assert.Equal((1, 1, 200m, 10m, 190m, 0m, 190m), Figures(byPayer[0]));
        Assert.Equal((2, 2, 400m, 0m, 80m, 320m, 400m), Figures(byPayer[1]));

        var byDay = WorkAmountAggregator.Rows(lines, line => line.RegisteredAtUtc.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), key => new WorkAmountGroupLabel(key, key), orderByKey: true);
        Assert.Equal(["2002-05-10", "2002-05-11"], byDay.Select(row => row.Key).ToArray());
        Assert.Equal((2, 2, 300m, 10m, 270m, 20m, 290m), Figures(byDay[0]));
        Assert.Equal((1, 1, 300m, 0m, 0m, 300m, 300m), Figures(byDay[1]));

        var total = WorkAmountAggregator.Total(lines);
        Assert.Equal((3, 3, 600m, 10m, 270m, 320m, 590m), Figures(total));
    }

    [DatabaseFact]
    public async Task The_letterhead_names_the_client_record_and_falls_back_to_the_default_name_for_the_platform_row()
    {
        var world = await WorldAsync();
        using (var scope = fixture.AppScope(world.Tenant))
        {
            var letterhead = await scope.ServiceProvider.GetRequiredService<IReportLetterhead>().GetAsync(world.Branch, CancellationToken.None);
            var name = TestTenants.LaboratoryNamePrefix + world.Tenant.ToString("N")[..8];   // the fixture's placeholder record
            Assert.Equal((name, name), (letterhead.LabNameEn, letterhead.LabNameAr));
            Assert.Null(letterhead.BranchNameEn);   // no Organization branch row was written for this branch id
        }

        // DbReviewTenantKeys: the platform tenant has a registry row (kind Platform) but is no laboratory: the default name stays.
        using var platform = fixture.AppScope(TenantIds.Platform);
        var fallback = await platform.ServiceProvider.GetRequiredService<IReportLetterhead>().GetAsync(world.Branch, CancellationToken.None);
        Assert.Equal(("Laboratory", "المختبر"), (fallback.LabNameEn, fallback.LabNameAr));
    }

    private static (int Visits, int Services, decimal Gross, decimal Discount, decimal Cash, decimal Credit, decimal Net) Figures(Contracts.Reporting.WorkAmountRow row) =>
        (row.Visits, row.Services, row.Gross, row.Discount, row.CashIncome, row.CreditIncome, row.Net);

    // ---- rows ----------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid OtherTenant, Guid Branch, Guid PayerCash, Guid PayerInsurer, Guid Doctor, Guid VisitA, Guid VisitB, Guid VisitC);

    private static async Task<World> WorldAsync()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (cash, insurer, doctor) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var visitA = await InsertVisitAsync(tenant, branch, insurer, Day.AddHours(8), doctor);
        await InsertLineAsync(tenant, visitA, "CBC", "Ordered", 100m, 80m, 20m, 0m, 1);
        await InsertLineAsync(tenant, visitA, "GLU", "Cancelled", 50m, 50m, 0m, 0m, 2);

        var visitB = await InsertVisitAsync(tenant, branch, cash, Day.AddHours(10), null);
        var package = await InsertLineAsync(tenant, visitB, "PKG", "Reserved", 200m, 190m, 0m, 10m, 1, kind: "Package");
        await InsertLineAsync(tenant, visitB, "PKM", "Reserved", 0m, 0m, 0m, 0m, 2, kind: "PackageMember", parentLineId: package);

        var visitC = await InsertVisitAsync(tenant, branch, insurer, Day.AddDays(1).AddHours(9), null);
        await InsertLineAsync(tenant, visitC, "TSH", "Ordered", 300m, 0m, 300m, 0m, 1);

        var visitD = await InsertVisitAsync(tenant, branch, cash, Day.AddHours(11), null);   // every service cancelled: contributes nothing
        await InsertLineAsync(tenant, visitD, "ALL", "Cancelled", 70m, 70m, 0m, 0m, 1);

        var otherBranch = await InsertVisitAsync(tenant, Guid.CreateVersion7(), cash, Day.AddHours(12), null);
        await InsertLineAsync(tenant, otherBranch, "OBR", "Ordered", 10m, 10m, 0m, 0m, 1);

        var later = await InsertVisitAsync(tenant, branch, cash, Day.AddDays(2), null);   // the window end is exclusive
        await InsertLineAsync(tenant, later, "LAT", "Ordered", 10m, 10m, 0m, 0m, 1);

        var theirs = await InsertVisitAsync(otherTenant, branch, cash, Day.AddHours(8), null);
        await InsertLineAsync(otherTenant, theirs, "OTH", "Ordered", 10m, 10m, 0m, 0m, 1);

        return new World(tenant, otherTenant, branch, cash, insurer, doctor, visitA, visitB, visitC);
    }

    private static async Task<Guid> InsertVisitAsync(Guid tenant, Guid branch, Guid payer, DateTimeOffset registeredAtUtc, Guid? doctor)
    {
        var id = Guid.CreateVersion7();
        await RawRows.InsertAsync("app.visits", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant,
            ["id"] = id,
            ["accession"] = $"ACC-WA-{id.ToString("N")[^10..].ToUpperInvariant()}",
            ["patient_id"] = Guid.CreateVersion7(),
            ["branch_id"] = branch,
            ["payer_id"] = payer,
            ["contract_tier_id"] = Guid.CreateVersion7(),
            ["referring_doctor_id"] = doctor,
            ["status"] = "Open",
            ["registered_at_utc"] = registeredAtUtc,
            ["registered_by_user_id"] = Guid.CreateVersion7(),
            ["urgent"] = false,
            ["total_list_price"] = 0m,
            ["discount_total"] = 0m,
            ["patient_share_total"] = 0m,
            ["payer_share_total"] = 0m,
        });
        return id;
    }

    private static async Task<Guid> InsertLineAsync(Guid tenant, Guid visit, string code, string status, decimal listPrice, decimal patientShare, decimal payerShare, decimal discount, int sequence, string kind = "Service", Guid? parentLineId = null)
    {
        var id = Guid.CreateVersion7();
        var cancelled = status == "Cancelled";
        await RawRows.InsertAsync("app.visit_lines", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant,
            ["id"] = id,
            ["visit_id"] = visit,
            ["service_id"] = Guid.CreateVersion7(),
            ["service_code"] = code,
            ["service_name_en"] = $"Service {code}",
            ["service_name_ar"] = $"خدمة {code}",
            ["service_type"] = "Test",
            ["kind"] = kind,
            ["parent_line_id"] = parentLineId,
            ["status"] = status,
            ["list_price"] = listPrice,
            ["patient_share"] = patientShare,
            ["payer_share"] = payerShare,
            ["discount"] = discount,
            ["origin"] = "Manual",
            ["sequence"] = sequence,
            ["ordered_at_utc"] = Day,
            ["ordered_by_user_id"] = Guid.CreateVersion7(),
            ["cancelled_at_utc"] = cancelled ? Day.AddHours(1) : null,
            ["cancellation_reason"] = cancelled ? "integration test" : null,
            ["cancelled_by_user_id"] = cancelled ? Guid.CreateVersion7() : null,
        });
        return id;
    }
}
