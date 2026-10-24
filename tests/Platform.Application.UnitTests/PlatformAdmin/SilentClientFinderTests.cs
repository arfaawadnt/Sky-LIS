using System.Text;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;

namespace Platform.Application.UnitTests.PlatformAdmin;

/// <summary>
/// LIS-CR-007 A-01: the silent clients of a window from the reader's rows. Registrations are asked only for the clients
/// the sign-ins did not clear; a registration inside the window clears a client; the longest silence comes first; the
/// window is checked by shape (VALIDATION-400 for 0 or 91); the CSV carries the BOM and both languages' headers.
/// </summary>
public sealed class SilentClientFinderTests
{
    private const string Base = "https://lis.example.test";
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task S_SIL_finder_lists_the_silent_clients_longest_silence_first_and_reads_registrations_only_for_candidates()
    {
        var signedInToday = Row("Today Lab", TenantStatus.Active, Now.AddDays(-30), Now.AddHours(-1));
        var registeredYesterday = Row("Busy Desk Lab", TenantStatus.Active, Now.AddDays(-30), Now.AddDays(-10));
        var fourDays = Row("Quiet Lab", TenantStatus.Active, Now.AddDays(-30), Now.AddDays(-4));
        var neverUsed = Row("Never Lab", TenantStatus.Trial, Now.AddDays(-20), null);
        var newborn = Row("New Lab", TenantStatus.Trial, Now.AddDays(-1), null);
        var reader = new FakeReader([signedInToday, registeredYesterday, fourDays, neverUsed, newborn])
        {
            Registrations = { [registeredYesterday.TenantId] = Now.AddDays(-1), [fourDays.TenantId] = Now.AddDays(-6) },
        };

        var silent = await SilentClientFinder.FindAsync(reader, Base, Now, 3, CancellationToken.None);

        Assert.Equal(["Never Lab", "Quiet Lab"], silent.Select(item => item.NameEn));
        Assert.DoesNotContain(signedInToday.TenantId, reader.AskedFor);
        Assert.DoesNotContain(newborn.TenantId, reader.AskedFor);
        Assert.Contains(registeredYesterday.TenantId, reader.AskedFor);

        var quiet = silent[1];
        Assert.Equal((Now.AddDays(-4), Now.AddDays(-6), Now.AddDays(-4), 4), (quiet.LastSignInAtUtc!.Value, quiet.LastRegistrationAtUtc!.Value, quiet.LastActivityAtUtc!.Value, quiet.SilentDays));
        Assert.Equal(($"{Base}/lab/quiet-lab", "Active", "Manager of Quiet Lab", "+201000000000"), (quiet.PortalUrl, quiet.Status, quiet.AdministratorName, quiet.AdministratorMobile));
        var never = silent[0];
        Assert.Equal(("Trial", null, 20), (never.Status, never.LastActivityAtUtc, never.SilentDays));
    }

    [Fact]
    public async Task S_SIL_finder_asks_nothing_more_when_no_client_is_a_candidate()
    {
        var reader = new FakeReader([Row("Today Lab", TenantStatus.Active, Now.AddDays(-30), Now)]);

        Assert.Empty(await SilentClientFinder.FindAsync(reader, Base, Now, 3, CancellationToken.None));
        Assert.Equal(0, reader.RegistrationCalls);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(90, true)]
    [InlineData(0, false)]
    [InlineData(91, false)]
    [InlineData(-3, false)]
    public void S_SIL_7_the_window_is_1_to_90_days_by_shape(int? days, bool valid)
    {
        Assert.Equal(valid, new ListSilentClientsValidator().Validate(new ListSilentClientsQuery(days)).IsValid);
        Assert.Equal(valid, new ExportSilentClientsValidator().Validate(new ExportSilentClientsQuery(days, "en")).IsValid);
        Assert.Equal(valid, new GetPlatformDashboardValidator().Validate(new GetPlatformDashboardQuery(null, days)).IsValid);
    }

    [Fact]
    public void S_SIL_csv_has_a_bom_localized_headers_and_neutralised_mobiles()
    {
        var item = new SilentClientItem(Guid.CreateVersion7(), "Quiet, Lab", "مختبر هادئ", $"{Base}/lab/quiet", "Active", Now.AddDays(-30), Now.AddDays(-4), null, Now.AddDays(-4), 4, "Mona", "+201001234567");

        var english = SilentClientsCsv.Write([item], arabic: false);
        var arabic = SilentClientsCsv.Write([item], arabic: true);

        Assert.Equal(Encoding.UTF8.GetPreamble(), english[..3]);
        var lines = Encoding.UTF8.GetString(english[3..]).Split("\r\n");
        Assert.StartsWith("Client (English),Client (Arabic),Portal address,Status", lines[0], StringComparison.Ordinal);
        Assert.Equal($"\"Quiet, Lab\",مختبر هادئ,{Base}/lab/quiet,Active,2026-09-24 09:00,2026-10-20 09:00,,2026-10-20 09:00,4,Mona,'+201001234567", lines[1]);
        Assert.StartsWith("العميل (إنجليزي)", Encoding.UTF8.GetString(arabic[3..]), StringComparison.Ordinal);
    }

    private static ClientActivityRow Row(string name, TenantStatus status, DateTimeOffset createdAtUtc, DateTimeOffset? lastSignInUtc) =>
        new(Guid.CreateVersion7(), name, name + " (ar)", name.ToLowerInvariant().Replace(' ', '-'), status, createdAtUtc, "Manager of " + name, "+201000000000", lastSignInUtc);

    private sealed class FakeReader(IReadOnlyList<ClientActivityRow> rows) : IClientLastActivityReader
    {
        public Dictionary<Guid, DateTimeOffset> Registrations { get; } = [];

        public List<Guid> AskedFor { get; } = [];

        public int RegistrationCalls { get; private set; }

        public Task<IReadOnlyList<ClientActivityRow>> ListClientsAsync(CancellationToken cancellationToken) => Task.FromResult(rows);

        public Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastRegistrationsAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken)
        {
            RegistrationCalls++;
            AskedFor.AddRange(tenantIds);
            IReadOnlyDictionary<Guid, DateTimeOffset> answer = Registrations.Where(pair => tenantIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            return Task.FromResult(answer);
        }
    }
}
