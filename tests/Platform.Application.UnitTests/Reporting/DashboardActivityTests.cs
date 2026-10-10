using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Account;
using Platform.Application.Account.Queries;
using Platform.Application.UnitTests.Support;
using Platform.Domain.Organization;
using Platform.Domain.Settings;
using Platform.Infrastructure.Persistence.Reporting;

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
    public async Task S_D2_without_the_results_table_the_three_figures_stay_not_available()
    {
        var reader = new FixedReader(new BranchDayFigures(null, null, null, null, null));
        var handler = new GetBranchDashboardHandler(reader, new DefaultSettings(), new NoPrivileges(), new SessionUser(Guid.CreateVersion7()), new NoTenant(), new FixedClock(Now));

        var dashboard = await handler.Handle(new GetBranchDashboardQuery(null, null), CancellationToken.None);

        Assert.Null(dashboard.Kpis.PendingResults);
        Assert.Null(dashboard.Kpis.UnverifiedBacklog);
        Assert.Null(dashboard.Kpis.DelayedTests);
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
