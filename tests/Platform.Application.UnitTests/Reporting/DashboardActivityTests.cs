using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Account;
using Platform.Application.Account.Queries;
using Platform.Application.UnitTests.Support;
using Platform.Contracts.Account;
using Platform.Domain.Account;
using Platform.Domain.Organization;
using Platform.Domain.Settings;
using Platform.Infrastructure.Persistence.Reporting;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Reporting;

/// <summary>
/// Release-1 scope of the Reporting module (intake LIS-INT-REP-001 §16): the CP-05 result figures reach the dashboard
/// response unchanged (S-D1, S-D2), and the SA-03 activity rows fold into the platform snapshot (S-A1, S-A2).
/// </summary>
public sealed class DashboardActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task S_D1_the_result_figures_of_the_reader_reach_the_dashboard_kpis()
    {
        var branch = Guid.CreateVersion7();
        var reader = new FixedReader(new BranchDayFigures(12, 3400m, 30, 1, new Dictionary<string, int>(), PendingResults: 41, UnverifiedBacklog: 17, DelayedTests: 4));
        var handler = new GetBranchDashboardHandler(reader, new DefaultSettings(), new NoPrivileges(), new SessionUser(branch), new NoTenant(), new FixedClock(Now));

        var dashboard = await handler.Handle(new GetBranchDashboardQuery("2026-10-10", 180), CancellationToken.None);

        Assert.Equal((41, 17, 4), (dashboard.Kpis.PendingResults, dashboard.Kpis.UnverifiedBacklog, dashboard.Kpis.DelayedTests));
        Assert.Equal((12, 30, 1), (dashboard.Kpis.Registrations, dashboard.Kpis.SamplesReceived, dashboard.Kpis.RejectedSamples));
        Assert.Equal(branch, reader.Branch);
    }

    [Fact]
    public async Task O9_the_collected_amount_reaches_only_a_user_who_may_see_payments()
    {
        var reader = new FixedReader(new BranchDayFigures(12, 3400m, 30, 1, new Dictionary<string, int>()));
        var query = new GetBranchDashboardQuery("2026-10-10", 0);

        var technician = await new GetBranchDashboardHandler(reader, new DefaultSettings(), new NoPrivileges(), new SessionUser(Guid.CreateVersion7()), new NoTenant(), new FixedClock(Now)).Handle(query, CancellationToken.None);
        var cashier = await new GetBranchDashboardHandler(reader, new DefaultSettings(), new KeyPrivileges("billing.view"), new SessionUser(Guid.CreateVersion7()), new NoTenant(), new FixedClock(Now)).Handle(query, CancellationToken.None);

        Assert.Null(technician.Kpis.CollectedToday);
        Assert.Equal(3400m, cashier.Kpis.CollectedToday);
    }

    [Fact]
    public async Task S_D1b_the_delayed_tests_of_the_reader_fill_the_dashboard_table()
    {
        // UAT R1-02: the tile counted the delayed tests while the table was always built empty.
        var delayed = new DelayedTestItem(Guid.CreateVersion7(), "ACC-2026-00012", "Mona Adel", "Glucose", Now.AddHours(-2), 120, "Ordered", "جلوكوز");
        var reader = new FixedReader(new BranchDayFigures(1, 0m, 0, 0, new Dictionary<string, int>(), 1, 0, 1, [delayed]));
        var handler = new GetBranchDashboardHandler(reader, new DefaultSettings(), new NoPrivileges(), new SessionUser(Guid.CreateVersion7()), new NoTenant(), new FixedClock(Now));

        var dashboard = await handler.Handle(new GetBranchDashboardQuery(null, null), CancellationToken.None);

        Assert.Equal([delayed], dashboard.DelayedTests);
        Assert.Equal(1, dashboard.Kpis.DelayedTests);
    }

    [Fact]
    public async Task A10_a_future_day_of_the_user_is_refused_and_today_in_the_user_s_offset_is_accepted()
    {
        // Validation A-10 (2026-10-11): 2026-10-20 was accepted and shown with figures "as of now".
        var reader = new FixedReader(new BranchDayFigures(null, null, null, null, null));
        var handler = new GetBranchDashboardHandler(reader, new DefaultSettings(), new NoPrivileges(), new SessionUser(Guid.CreateVersion7()), new NoTenant(), new FixedClock(Now));

        var future = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new GetBranchDashboardQuery("2026-10-20", 180), CancellationToken.None));
        Assert.Equal(AccountCodes.DashboardDayInFuture, future.Code);

        // 09:30 UTC: still the 10th at +14 h (23:30), so the 11th is the future in every allowed offset.
        var tomorrowUtc = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new GetBranchDashboardQuery("2026-10-11", 0), CancellationToken.None));
        Assert.Equal(AccountCodes.DashboardDayInFuture, tomorrowUtc.Code);
        Assert.Equal("2026-10-10", (await handler.Handle(new GetBranchDashboardQuery("2026-10-10", 840), CancellationToken.None)).Date);
        Assert.Equal("2026-10-09", (await handler.Handle(new GetBranchDashboardQuery("2026-10-09", -600), CancellationToken.None)).Date);
        Assert.Equal("2026-10-10", (await handler.Handle(new GetBranchDashboardQuery(null, 180), CancellationToken.None)).Date);
    }

    [Fact]
    public async Task S_D2_without_the_results_table_the_three_figures_stay_not_available()
    {
        var reader = new FixedReader(new BranchDayFigures(null, null, null, null, null));
        var handler = new GetBranchDashboardHandler(reader, new DefaultSettings(), new NoPrivileges(), new SessionUser(Guid.CreateVersion7()), new NoTenant(), new FixedClock(Now));

        var dashboard = await handler.Handle(new GetBranchDashboardQuery(null, null), CancellationToken.None);

        Assert.Null(dashboard.Kpis.PendingResults);
        Assert.Null(dashboard.Kpis.UnverifiedBacklog);
        Assert.Null(dashboard.Kpis.DelayedTests);
        Assert.Empty(dashboard.DelayedTests);
    }

    [Fact]
    public void S_A1_activity_rows_add_up_and_list_the_clients_by_registrations()
    {
        var quiet = Guid.CreateVersion7();
        var busy = Guid.CreateVersion7();
        var idle = Guid.CreateVersion7();
        var snapshot = ClientActivityReader.Snapshot(
        [
            new ClientActivityReader.ActivityRow(quiet, 3, 9, 4),
            new ClientActivityReader.ActivityRow(busy, 20, 75, 31),
            new ClientActivityReader.ActivityRow(idle, 0, 0, 0),
        ]);

        Assert.Equal((23, 84, 35), (snapshot.ServedPatients, snapshot.Tests, snapshot.Registrations));
        Assert.Equal([busy, quiet], snapshot.ByClient.Select(item => item.TenantId).ToArray());
        Assert.Equal([31, 4], snapshot.ByClient.Select(item => item.Registrations).ToArray());
    }

    [Fact]
    public void S_A2_no_activity_is_the_empty_snapshot_and_huge_counts_are_clamped()
    {
        Assert.Same(Platform.Application.PlatformAdmin.ClientActivitySnapshot.Empty, ClientActivityReader.Snapshot([]));

        var snapshot = ClientActivityReader.Snapshot([new ClientActivityReader.ActivityRow(Guid.CreateVersion7(), long.MaxValue, 1, 1)]);
        Assert.Equal(int.MaxValue, snapshot.ServedPatients);
    }

    private sealed class FixedReader(BranchDayFigures figures) : IBranchDashboardReader
    {
        public Guid? Branch { get; private set; }

        public Task<BranchDayFigures> ReadAsync(Guid branchId, DashboardWindow window, CancellationToken cancellationToken)
        {
            Branch = branchId;
            return Task.FromResult(figures);
        }

        public Task<(decimal Balance, string CurrencyCode)?> ReadWalletAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<(decimal Balance, string CurrencyCode)?>(null);
    }

    private sealed class DefaultSettings : ITenantSettings
    {
        public ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken) => ValueTask.FromResult(descriptor.Default);

        public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    private sealed class KeyPrivileges(params string[] keys) : IUserPrivileges
    {
        public IReadOnlySet<string> PermissionKeys { get; } = new HashSet<string>(keys);

        public SpecialRights SpecialRights => SpecialRights.None;

        public IReadOnlyList<ResultRight> ResultRights { get; } = [];

        public bool Has(string permissionKey) => PermissionKeys.Contains(permissionKey);

        public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => false;
    }

    private sealed class NoPrivileges : IUserPrivileges
    {
        public IReadOnlySet<string> PermissionKeys { get; } = new HashSet<string>();

        public SpecialRights SpecialRights => SpecialRights.None;

        public IReadOnlyList<ResultRight> ResultRights { get; } = [];

        public bool Has(string permissionKey) => false;

        public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => false;
    }

    private sealed class SessionUser(Guid branchId) : ICurrentUser
    {
        public bool IsPrimaryAdministrator { get; set; }
        public bool IsAuthenticated => true;

        public Guid? UserId { get; } = Guid.CreateVersion7();

        public Guid? SessionId => null;

        public string? DisplayName => "Lab Manager";

        public Guid? BranchId => branchId;
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? TenantId => null;

        public bool IsElevated => false;
    }
}
