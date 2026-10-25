using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Account.Queries;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Application.Settings;
using Platform.Application.Settings.Commands;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Contracts.Settings;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.PlatformAdmin;

/// <summary>
/// LIS-CR-013 (C-17): what a laboratory is shown — the live announcements of a placement its audience admits, unless the client
/// opted out (read in the client's own tenant scope) — and the shape rules of the requests.
/// </summary>
public sealed class AnnouncementFeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Egypt = Guid.CreateVersion7();
    private static readonly Guid Starter = Guid.CreateVersion7();

    private readonly FakeAnnouncements announcements = new();
    private readonly FakeTenantScope scope = new();
    private readonly FakeSettings settings;
    private readonly FakeDirectory directory = new();
    private readonly FakeLaboratories laboratories = new();
    private readonly FixedClock clock = new(Now);

    public AnnouncementFeedTests() => settings = new FakeSettings(scope);

    private Announcement Add(string title, AnnouncementPlacement placement, AnnouncementAudience? audience = null, bool active = true, int startedDaysAgo = 1)
    {
        var announcement = Announcement.Create(new AnnouncementContent(title, title + " ar", null, null, null), placement, new AnnouncementSchedule(Now.AddDays(-startedDaysAgo), null), audience ?? AnnouncementAudience.Everyone);
        if (active)
        {
            announcement.Activate(Now);
        }

        announcements.Rows.Add(announcement);
        return announcement;
    }

    private AnnouncementViewer Laboratory(string slug, TenantStatus status = TenantStatus.Active)
    {
        var viewer = new AnnouncementViewer(Guid.CreateVersion7(), Egypt, Starter, status);
        directory.Viewers[viewer.TenantId] = viewer;
        laboratories.Records[slug] = new PublicLaboratoryRecord(viewer.TenantId, slug, "Lab", "مختبر", null);
        return viewer;
    }

    private ListPublicAnnouncementsHandler PublicHandler() => new(announcements, directory, laboratories, settings, scope, clock);

    [Fact]
    public async Task The_sign_in_list_holds_the_live_SignIn_announcements_the_laboratory_audience_admits_newest_first()
    {
        Laboratory("al-noor");
        Add("older", AnnouncementPlacement.SignIn, startedDaysAgo: 3);
        Add("newer", AnnouncementPlacement.SignIn, startedDaysAgo: 1);
        Add("draft", AnnouncementPlacement.SignIn, active: false);
        Add("welcome", AnnouncementPlacement.Welcome);
        Add("elsewhere", AnnouncementPlacement.SignIn, new AnnouncementAudience(Guid.CreateVersion7(), null, AnnouncementAudienceStatus.Any, []));

        var shown = await PublicHandler().Handle(new ListPublicAnnouncementsQuery("SignIn", "Al-Noor"), CancellationToken.None);

        Assert.Equal(["newer", "older"], shown.Select(item => item.TitleEn));
    }

    [Fact]
    public async Task Without_a_laboratory_only_open_audiences_are_shown_and_an_unknown_or_malformed_address_shows_nothing()
    {
        Add("open", AnnouncementPlacement.SignIn);
        Add("trial only", AnnouncementPlacement.SignIn, new AnnouncementAudience(null, null, AnnouncementAudienceStatus.Trial, []));

        Assert.Equal(["open"], (await PublicHandler().Handle(new ListPublicAnnouncementsQuery(null, null), CancellationToken.None)).Select(item => item.TitleEn));
        Assert.Empty(await PublicHandler().Handle(new ListPublicAnnouncementsQuery(null, "no-such-lab"), CancellationToken.None));
        Assert.Empty(await PublicHandler().Handle(new ListPublicAnnouncementsQuery(null, "bad slug!"), CancellationToken.None));
    }

    [Fact]
    public async Task A_client_that_opted_out_sees_none_and_its_setting_is_read_in_its_own_tenant_scope()
    {
        var viewer = Laboratory("al-noor");
        Add("offer", AnnouncementPlacement.SignIn);
        settings.Hidden.Add(viewer.TenantId);

        Assert.Empty(await PublicHandler().Handle(new ListPublicAnnouncementsQuery("SignIn", "al-noor"), CancellationToken.None));
        Assert.Contains(viewer.TenantId, settings.ReadIn);
        Assert.Null(scope.TenantId);   // the scope was restored
    }

    [Fact]
    public async Task At_most_five_announcements_are_shown()
    {
        for (var index = 0; index < 7; index++)
        {
            Add($"a{index}", AnnouncementPlacement.SignIn, startedDaysAgo: index + 1);
        }

        Assert.Equal(AnnouncementFeed.MaxShown, (await PublicHandler().Handle(new ListPublicAnnouncementsQuery(null, null), CancellationToken.None)).Count);
    }

    [Fact]
    public async Task The_dashboard_card_reads_the_Welcome_announcements_of_the_session_laboratory_and_none_in_the_platform_tenant()
    {
        var viewer = Laboratory("al-noor", TenantStatus.Trial);
        Add("sign-in", AnnouncementPlacement.SignIn);
        Add("welcome trial", AnnouncementPlacement.Welcome, new AnnouncementAudience(Egypt, null, AnnouncementAudienceStatus.Trial, [viewer.TenantId]));

        var own = await new GetMyAnnouncementsHandler(announcements, directory, settings, scope, new FakeTenant(viewer.TenantId), clock).Handle(new GetMyAnnouncementsQuery(), CancellationToken.None);
        Assert.Equal(["welcome trial"], own.Select(item => item.TitleEn));

        Assert.Empty(await new GetMyAnnouncementsHandler(announcements, directory, settings, scope, new FakeTenant(TenantIds.Platform), clock).Handle(new GetMyAnnouncementsQuery(), CancellationToken.None));
        Assert.Empty(await new GetMyAnnouncementsHandler(announcements, directory, settings, scope, new FakeTenant(Guid.CreateVersion7()), clock).Handle(new GetMyAnnouncementsQuery(), CancellationToken.None));
    }

    [Fact]
    public void A_public_list_serves_the_SignIn_placement_only()
    {
        var validator = new ListPublicAnnouncementsValidator();
        Assert.True(validator.Validate(new ListPublicAnnouncementsQuery(null, "al-noor")).IsValid);
        Assert.True(validator.Validate(new ListPublicAnnouncementsQuery("signin", null)).IsValid);
        Assert.False(validator.Validate(new ListPublicAnnouncementsQuery("Welcome", null)).IsValid);
    }

    [Fact]
    public async Task The_Parameters_pages_never_write_the_client_opt_out_SET_004()
    {
        var handler = new UpdateTenantSettingsCommandHandler(settings, null!, null!, new FakeTenant(Guid.CreateVersion7()), clock);
        foreach (var value in new[] { "true", null })
        {
            var refusal = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new UpdateTenantSettingsCommand([new SettingValueItem(SettingKeys.HideAnnouncements.Key, value)]), CancellationToken.None));
            Assert.Equal((SettingCodes.SetByPlatformOnly, DomainFault.Refused), (refusal.Code, refusal.Fault));
        }
    }

    [Theory]
    [InlineData("SignIn", "Any", true)]
    [InlineData("welcome", null, true)]
    [InlineData("Banner", null, false)]
    [InlineData("SignIn", "Inactive", false)]
    public void The_request_shape_names_a_placement_and_an_audience_status(string placement, string? status, bool valid)
    {
        var request = new AnnouncementRequest("Offer", "عرض", null, null, null, placement, Now, null, null, null, status, null);
        Assert.Equal(valid, new CreateAnnouncementValidator().Validate(new CreateAnnouncementCommand(request)).IsValid);
    }

    private sealed class FakeAnnouncements : IAnnouncementRepository
    {
        public List<Announcement> Rows { get; } = [];

        public Task<Announcement?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

        public void Add(Announcement aggregate) => Rows.Add(aggregate);

        public Task<Paged<AnnouncementView>> ListAsync(AnnouncementFilter filter, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AnnouncementView?> GetViewAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Announcement?> FindAsync(Guid id, CancellationToken cancellationToken) => GetForUpdateAsync(id, cancellationToken);

        public Task<IReadOnlyList<Announcement>> ListLiveAsync(AnnouncementPlacement placement, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Announcement>>(Rows.Where(row => row.Placement == placement && row.IsLiveAt(nowUtc)).OrderByDescending(row => row.FromUtc).ToList());
    }

    private sealed class FakeDirectory : IAnnouncementAudienceDirectory
    {
        public Dictionary<Guid, AnnouncementViewer> Viewers { get; } = [];

        public Task<AnnouncementViewer?> FindViewerAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Viewers.GetValueOrDefault(tenantId));

        public Task<bool> CountryExistsAsync(Guid countryId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<Guid>> MissingClientsAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class FakeLaboratories : IPublicLaboratoryDirectory
    {
        public Dictionary<string, PublicLaboratoryRecord> Records { get; } = [];

        public Task<PublicLaboratoryRecord?> FindUsableAsync(string slug, CancellationToken cancellationToken) => Task.FromResult(Records.GetValueOrDefault(slug));
    }

    /// <summary>The stored rows of the tenant of the current scope (<c>hideAnnouncements = true</c> for a hidden one), recording which scopes asked.</summary>
    private sealed class FakeSettings(FakeTenantScope scope) : ITenantSettingsStore
    {
        public HashSet<Guid> Hidden { get; } = [];

        public List<Guid> ReadIn { get; } = [];

        public Task<IReadOnlyList<StoredSetting>> ListAsync(CancellationToken cancellationToken)
        {
            var tenant = scope.TenantId ?? Guid.Empty;
            ReadIn.Add(tenant);
            IReadOnlyList<StoredSetting> rows = Hidden.Contains(tenant) ? [new StoredSetting(SettingKeys.HideAnnouncements.Key, "true", Now, null)] : [];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<StoredSetting>> ListInheritedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpsertAsync(string key, string value, Guid? actorUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeTenantScope : ICurrentTenantSetter
    {
        public Guid? TenantId { get; private set; }

        public IDisposable ForTenant(Guid tenantId)
        {
            var previous = TenantId;
            TenantId = tenantId;
            return new Restore(() => TenantId = previous);
        }

        public IDisposable Elevate() => throw new NotSupportedException();

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }

    private sealed class FakeTenant(Guid? tenantId) : ICurrentTenant
    {
        public Guid? TenantId { get; } = tenantId;

        public bool IsElevated => false;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
