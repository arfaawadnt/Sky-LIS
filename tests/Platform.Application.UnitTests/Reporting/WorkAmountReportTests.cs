using System.Text;
using Platform.Application.Abstractions.Security;
using Platform.Application.Patients;
using Platform.Application.Reporting;
using Platform.Application.Reporting.Queries;
using Platform.Application.UnitTests.Support;
using Platform.Contracts.Common;
using Platform.Contracts.Reporting;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Reporting;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Reporting;

/// <summary>
/// Work amount report (change request 2026-10-13 item 3; intake LIS-INT-REP-001 §16 scenarios S-W1…S-W8): the default
/// period is today in the laboratory's zone, the member's days limit refuses an earlier start (REP-001), the branch rule,
/// the arithmetic of the groupings (visits counted once, net = cash + credit) and the CSV shape.
/// </summary>
public sealed class WorkAmountReportTests
{
    // 22:30 UTC on the 13th is already the 14th, 01:30, in a +03:00 laboratory.
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 22, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+03", TimeSpan.FromHours(3), "Test +03", "Test +03");
    private static readonly Guid SessionBranch = Guid.CreateVersion7();
    private static readonly Guid PayerCash = Guid.CreateVersion7();
    private static readonly Guid PayerInsurer = Guid.CreateVersion7();

    [Fact]
    public async Task S_W1_the_default_period_is_today_in_the_laboratory_zone_and_the_window_is_the_whole_local_day()
    {
        var reader = new FakeReader([]);
        var world = new World(reader);

        var report = await world.Handler().Handle(new WorkAmountReportQuery(null, null, null, null), CancellationToken.None);

        Assert.Equal((new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14), "Day", SessionBranch, "Test+03"), (report.FromDay, report.ToDay, report.GroupBy, report.BranchId, report.TimeZone));
        Assert.Equal((new DateTimeOffset(2026, 10, 13, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero)), reader.Window);
        Assert.Equal(SessionBranch, reader.Branch);
        Assert.Empty(report.Rows);
        Assert.Equal((0, 0, 0m), (report.Totals.Visits, report.Totals.Services, report.Totals.Net));
        Assert.Equal(("Sky Lab", "مختبر سكاي", "Main"), (report.Letterhead.LabNameEn, report.Letterhead.LabNameAr, report.Letterhead.BranchNameEn));
    }

    [Fact]
    public async Task S_W2_a_period_before_the_member_s_earliest_day_is_refused_with_REP_001()
    {
        var world = new World(new FakeReader([])) { ReportDaysLimit = 0 };   // today only

        var refusal = await Assert.ThrowsAsync<DomainException>(() => world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14), "Payer", null), CancellationToken.None));

        Assert.Equal(ReportingCodes.ReportPeriodBeyondLimit, refusal.Code);
        Assert.Equal(DomainFault.Refused, refusal.Fault);

        // the same member may report today, and a member without a limit may report any past day
        Assert.Equal(new DateOnly(2026, 10, 14), (await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "Payer", null), CancellationToken.None)).FromDay);
        world.ReportDaysLimit = null;
        Assert.Equal(new DateOnly(2025, 1, 1), (await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), "Payer", null), CancellationToken.None)).FromDay);
    }

    [Fact]
    public async Task S_W3_a_period_ending_before_it_starts_or_longer_than_a_year_is_refused_with_REP_002()
    {
        var world = new World(new FakeReader([]));

        var reversed = await Assert.ThrowsAsync<DomainException>(() => world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 9), null, null), CancellationToken.None));
        Assert.Equal(ReportingCodes.ReportPeriodInvalid, reversed.Code);

        var tooLong = await Assert.ThrowsAsync<DomainException>(() => world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2), null, null), CancellationToken.None));
        Assert.Equal(ReportingCodes.ReportPeriodInvalid, tooLong.Code);

        // a single bound names a one-day period
        var single = await world.Handler().Handle(new WorkAmountReportQuery(null, new DateOnly(2026, 10, 2), null, null), CancellationToken.None);
        Assert.Equal((new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2)), (single.FromDay, single.ToDay));
    }

    [Fact]
    public async Task S_W4_another_branch_needs_organization_view_or_an_assignment_to_it()
    {
        var other = Guid.CreateVersion7();
        var reader = new FakeReader([]);
        var world = new World(reader);

        var refused = await Assert.ThrowsAsync<DomainException>(() => world.Handler().Handle(new WorkAmountReportQuery(null, null, null, other), CancellationToken.None));
        Assert.Equal(ReportingCodes.ReportBranchNotAllowed, refused.Code);
        Assert.Equal(DomainFault.Refused, refused.Fault);

        world.AssignedBranches = [SessionBranch, other];
        Assert.Equal(other, (await world.Handler().Handle(new WorkAmountReportQuery(null, null, null, other), CancellationToken.None)).BranchId);

        world.AssignedBranches = [SessionBranch];
        world.Permissions.Add("organization.view");
        Assert.Equal(other, (await world.Handler().Handle(new WorkAmountReportQuery(null, null, null, other), CancellationToken.None)).BranchId);
        Assert.Equal(other, reader.Branch);

        // the session branch itself never needs a right; without a session branch the module refuses (TENANT-004)
        world.Permissions.Clear();
        Assert.Equal(SessionBranch, (await world.Handler().Handle(new WorkAmountReportQuery(null, null, null, SessionBranch), CancellationToken.None)).BranchId);
        world.User.BranchId = null;
        var noBranch = await Assert.ThrowsAsync<DomainException>(() => world.Handler().Handle(new WorkAmountReportQuery(null, null, null, null), CancellationToken.None));
        Assert.Equal(ProblemCodes.BranchRequired, noBranch.Code);
    }

    [Fact]
    public async Task S_W5_grouping_by_payer_counts_each_visit_once_per_group_and_adds_cash_and_credit()
    {
        var (visitA, visitB, visitC) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var lines = new List<WorkAmountLine>
        {
            Line(visitA, PayerInsurer, 100m, 20m, 80m, 0m, "CBC"),
            Line(visitA, PayerInsurer, 50m, 10m, 40m, 0m, "GLU"),
            Line(visitB, PayerCash, 200m, 190m, 0m, 10m, "LIP"),
            Line(visitC, PayerInsurer, 300m, 0m, 300m, 0m, "TSH"),
        };
        var world = new World(new FakeReader(lines));

        var report = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "payer", null), CancellationToken.None);

        Assert.Equal("Payer", report.GroupBy);
        Assert.Equal(["Cash patients", "Insurer One"], report.Rows.Select(row => row.LabelEn).ToArray());   // by label, not by key
        var cash = report.Rows[0];
        Assert.Equal((1, 1, 200m, 10m, 190m, 0m, 190m), (cash.Visits, cash.Services, cash.Gross, cash.Discount, cash.CashIncome, cash.CreditIncome, cash.Net));
        Assert.Equal("الدفع النقدي", cash.LabelAr);
        var insurer = report.Rows[1];
        Assert.Equal((2, 3, 450m, 0m, 30m, 420m, 450m), (insurer.Visits, insurer.Services, insurer.Gross, insurer.Discount, insurer.CashIncome, insurer.CreditIncome, insurer.Net));
        Assert.Equal((3, 4, 650m, 10m, 220m, 420m, 640m), (report.Totals.Visits, report.Totals.Services, report.Totals.Gross, report.Totals.Discount, report.Totals.CashIncome, report.Totals.CreditIncome, report.Totals.Net));
        Assert.Equal(WorkAmountAggregator.TotalKey, report.Totals.Key);
    }

    [Fact]
    public async Task S_W6_grouping_by_day_uses_the_laboratory_calendar_and_orders_the_days()
    {
        var lines = new List<WorkAmountLine>
        {
            Line(Guid.CreateVersion7(), PayerCash, 10m, 10m, 0m, 0m, "A", new DateTimeOffset(2026, 10, 13, 22, 10, 0, TimeSpan.Zero)),   // the 14th at +03
            Line(Guid.CreateVersion7(), PayerCash, 20m, 20m, 0m, 0m, "B", new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero)),
            Line(Guid.CreateVersion7(), PayerCash, 30m, 30m, 0m, 0m, "C", new DateTimeOffset(2026, 10, 12, 20, 59, 0, TimeSpan.Zero)),   // still the 12th at +03 (23:59)
        };
        var world = new World(new FakeReader(lines));

        var report = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14), "Day", null), CancellationToken.None);

        Assert.Equal(["2026-10-12", "2026-10-14"], report.Rows.Select(row => row.Key).ToArray());
        Assert.Equal([2, 1], report.Rows.Select(row => row.Visits).ToArray());
        Assert.Equal([50m, 10m], report.Rows.Select(row => row.Net).ToArray());
        Assert.Equal(report.Rows[0].Key, report.Rows[0].LabelAr);
    }

    [Fact]
    public async Task S_W7_grouping_by_doctor_clinical_group_tier_user_and_service_captions_every_row()
    {
        var doctor = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        var (serviceA, serviceB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var user = Guid.CreateVersion7();
        var tier = Guid.CreateVersion7();
        var lines = new List<WorkAmountLine>
        {
            new(Guid.CreateVersion7(), Now, SessionBranch, user, PayerCash, tier, doctor, serviceA, "CBC", "Complete blood count", "صورة دم", 100m, 100m, 0m, 0m),
            new(Guid.CreateVersion7(), Now, SessionBranch, user, PayerCash, tier, null, serviceB, "XRAY", "X-ray", "أشعة", 40m, 40m, 0m, 0m),
        };
        var names = new FakeNames
        {
            Doctors = { [doctor] = new WorkAmountGroupLabel("Dr. Hassan", "د. حسن", "D-01") },
            GroupOfService = { [serviceA] = group },
            Groups = { [group] = new WorkAmountGroupLabel("Haematology", "أمراض الدم", "HEM") },
            Users = { [user] = new WorkAmountGroupLabel("Reception One", "استقبال ١") },
            Tiers = { [tier] = new WorkAmountGroupLabel("Cash patients · Standard", "الدفع النقدي · قياسي") },
            Branches = { [SessionBranch] = new WorkAmountGroupLabel("Main", "الرئيسي", "MAIN") },
        };
        var world = new World(new FakeReader(lines), names);

        var doctors = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "Doctor", null), CancellationToken.None);
        Assert.Equal([("Dr. Hassan", "D-01", 100m), ("No referring doctor", null, 40m)], doctors.Rows.Select(row => (row.LabelEn, row.Code, row.Net)).ToArray());
        Assert.Equal("بدون طبيب محوِّل", doctors.Rows[1].LabelAr);

        var groups = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "ClinicalGroup", null), CancellationToken.None);
        Assert.Equal([("Haematology", "HEM", 100m), ("Not grouped", null, 40m)], groups.Rows.Select(row => (row.LabelEn, row.Code, row.Net)).ToArray());

        var tiers = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "ContractTier", null), CancellationToken.None);
        Assert.Equal([("Cash patients · Standard", 2, 140m)], tiers.Rows.Select(row => (row.LabelEn, row.Visits, row.Net)).ToArray());

        var users = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "User", null), CancellationToken.None);
        Assert.Equal([("Reception One", "استقبال ١", 2)], users.Rows.Select(row => (row.LabelEn, row.LabelAr, row.Services)).ToArray());

        var services = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "Service", null), CancellationToken.None);
        Assert.Equal([("Complete blood count", "CBC"), ("X-ray", "XRAY")], services.Rows.Select(row => (row.LabelEn, row.Code)).ToArray());

        // an id the ports no longer know is captioned with the id itself, never dropped
        var unknownUser = await new World(new FakeReader(lines), new FakeNames()).Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "User", null), CancellationToken.None);
        Assert.Equal(user.ToString(), unknownUser.Rows.Single().LabelEn);

        var branches = await world.Handler().Handle(new WorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "Branch", null), CancellationToken.None);
        Assert.Equal([("Main", "MAIN", 140m)], branches.Rows.Select(row => (row.LabelEn, row.Code, row.Net)).ToArray());
    }

    [Fact]
    public async Task S_W8_the_export_writes_a_formula_safe_utf8_csv_with_the_rows_and_the_total()
    {
        var lines = new List<WorkAmountLine>
        {
            Line(Guid.CreateVersion7(), PayerCash, 100m, 90m, 0m, 10m, "A"),
            Line(Guid.CreateVersion7(), PayerInsurer, 50m, 0m, 50m, 0m, "B"),
        };
        var names = new FakeNames();
        names.Payers[PayerCash] = new WorkAmountGroupLabel("=Cash, \"walk-in\"", "نقدي");
        names.Payers[PayerInsurer] = new WorkAmountGroupLabel("Insurer", "شركة");
        var world = new World(new FakeReader(lines), names);

        var file = await world.ExportHandler().Handle(new ExportWorkAmountReportQuery(new DateOnly(2026, 10, 14), null, "Payer", null), CancellationToken.None);

        Assert.Equal("work-amount-2026-10-14-2026-10-14-payer.csv", file.FileName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(file.Content.AsSpan(3));
        var rows = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Group,Group (Arabic),Code,Visits,Services,Gross,Discount,Cash income,Credit income,Net", rows[0]);
        Assert.Equal("\"'=Cash, \"\"walk-in\"\"\",نقدي,,1,1,100.00,10.00,90.00,0.00,90.00", rows[1]);
        Assert.Equal("Insurer,شركة,,1,1,50.00,0.00,0.00,50.00,50.00", rows[2]);
        Assert.Equal("Total,الإجمالي,,2,2,150.00,10.00,90.00,50.00,140.00", rows[3]);
        Assert.Equal(4, rows.Length);
    }

    private static WorkAmountLine Line(Guid visitId, Guid payerId, decimal listPrice, decimal patientShare, decimal payerShare, decimal discount, string code, DateTimeOffset? registeredAtUtc = null) =>
        new(visitId, registeredAtUtc ?? Now, SessionBranch, Guid.CreateVersion7(), payerId, Guid.CreateVersion7(), null, Guid.CreateVersion7(), code, code, code, listPrice, patientShare, payerShare, discount);

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed class World(FakeReader reader, FakeNames? names = null)
    {
        public FakeUser User { get; } = new() { BranchId = SessionBranch };

        public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

        public int? ReportDaysLimit { get; set; }

        public IReadOnlyList<Guid> AssignedBranches { get; set; } = [SessionBranch];

        public FakeNames Names { get; } = names ?? new FakeNames
        {
            Payers = { [PayerCash] = new WorkAmountGroupLabel("Cash patients", "الدفع النقدي"), [PayerInsurer] = new WorkAmountGroupLabel("Insurer One", "شركة تأمين") },
            Branches = { [SessionBranch] = new WorkAmountGroupLabel("Main", "الرئيسي", "MAIN") },
        };

        public WorkAmountReportHandler Handler() => new(reader, Names, new FakeLetterhead(), new FakeZone(), new FixedClock(Now), User, Privileges(), Principals());

        public ExportWorkAmountReportHandler ExportHandler() => new(reader, Names, new FakeLetterhead(), new FakeZone(), new FixedClock(Now), User, Privileges(), Principals());

        private FakePrivileges Privileges() => new(Permissions, new SpecialRights(null, false, false, false, false, false, false, false, false, DemographicsEditLimit.Complete, false, ReportDaysLimit));

        private FakePrincipals Principals() => new(AssignedBranches);
    }

    private sealed class FakeReader(IReadOnlyList<WorkAmountLine> lines) : IWorkAmountReader
    {
        public Guid? Branch { get; private set; }

        public (DateTimeOffset FromUtc, DateTimeOffset ToUtc)? Window { get; private set; }

        public Task<IReadOnlyList<WorkAmountLine>> ReadAsync(Guid branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        {
            Branch = branchId;
            Window = (fromUtc, toUtc);
            return Task.FromResult<IReadOnlyList<WorkAmountLine>>(lines.Where(line => line.RegisteredAtUtc >= fromUtc && line.RegisteredAtUtc < toUtc).ToList());
        }
    }

    private sealed class FakeNames : IWorkAmountNames
    {
        public Dictionary<Guid, WorkAmountGroupLabel> Branches { get; } = [];

        public Dictionary<Guid, WorkAmountGroupLabel> Users { get; } = [];

        public Dictionary<Guid, WorkAmountGroupLabel> Payers { get; } = [];

        public Dictionary<Guid, WorkAmountGroupLabel> Tiers { get; } = [];

        public Dictionary<Guid, WorkAmountGroupLabel> Doctors { get; } = [];

        public Dictionary<Guid, Guid> GroupOfService { get; } = [];

        public Dictionary<Guid, WorkAmountGroupLabel> Groups { get; } = [];

        public Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> BranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken) => Pick(Branches, branchIds);

        public Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> UsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) => Pick(Users, userIds);

        public Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> PayersAsync(IReadOnlyCollection<Guid> payerIds, CancellationToken cancellationToken) => Pick(Payers, payerIds);

        public Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> ContractTiersAsync(IReadOnlyCollection<Guid> contractTierIds, CancellationToken cancellationToken) => Pick(Tiers, contractTierIds);

        public Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> DoctorsAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken) => Pick(Doctors, doctorIds);

        public Task<IReadOnlyDictionary<Guid, Guid>> ClinicalGroupOfServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(GroupOfService.Where(pair => serviceIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));

        public Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> ClinicalGroupsAsync(IReadOnlyCollection<Guid> clinicalGroupIds, CancellationToken cancellationToken) => Pick(Groups, clinicalGroupIds);

        private static Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> Pick(Dictionary<Guid, WorkAmountGroupLabel> source, IReadOnlyCollection<Guid> ids) =>
            Task.FromResult<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>>(source.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private sealed class FakeLetterhead : IReportLetterhead
    {
        public Task<WorkAmountLetterhead> GetAsync(Guid branchId, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkAmountLetterhead("Sky Lab", "مختبر سكاي", branchId == SessionBranch ? "Main" : null, branchId == SessionBranch ? "الرئيسي" : null));
    }

    private sealed class FakeZone : ILaboratoryTimeZone
    {
        public Task<TimeZoneInfo?> FindAsync(CancellationToken cancellationToken) => Task.FromResult<TimeZoneInfo?>(Zone);
    }

    private sealed class FakeUser : ICurrentUser
    {
        public bool IsPrimaryAdministrator { get; set; }
        public bool IsAuthenticated => true;

        public Guid? UserId { get; } = Guid.CreateVersion7();

        public Guid? SessionId { get; } = Guid.CreateVersion7();

        public string? DisplayName => "Reporter";

        public Guid? BranchId { get; set; }
    }

    private sealed class FakePrivileges(IReadOnlySet<string> keys, SpecialRights rights) : IUserPrivileges
    {
        public bool Has(string permissionKey) => keys.Contains(permissionKey);

        public IReadOnlySet<string> PermissionKeys => keys;

        public SpecialRights SpecialRights => rights;

        public IReadOnlyList<ResultRight> ResultRights => [];

        public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => false;
    }

    private sealed class FakePrincipals(IReadOnlyList<Guid> branchIds) : ISessionPrincipalQuery
    {
        public Task<SessionPrincipal?> ResolveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<SessionPrincipal?>(new SessionPrincipal(sessionId, userId, "Reporter", true, Guid.CreateVersion7(), SessionKind.Browser, SessionScope.Full, Now.AddHours(1), Now.AddDays(1), false, null, true, "en", null, new HashSet<string>(StringComparer.Ordinal), branchIds, [], SessionBranch));

        public void Evict(Guid sessionId)
        {
        }
    }
}
