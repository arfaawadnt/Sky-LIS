using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.Infrastructure.Persistence.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

// Change request LIS-CR-012 (C-16, D-3): the reminder log and the reads of the subscription state. Platform tables (schema
// saas, no tenant, no RLS): the adapters never elevate. The migration and the grants are the integrator's (ReviewFeatures +
// PlatformAdmin.migration.sql §6).

/// <summary>
/// <c>saas.subscription_reminders</c>: one row per reminder sent, unique on (client, due key) so a reminder is never sent twice
/// (INV-PLT-050). Append-only by use (no update path; lis_app gets SELECT, INSERT).
/// </summary>
internal sealed class SubscriptionReminderConfiguration : IEntityTypeConfiguration<SubscriptionReminder>
{
    public void Configure(EntityTypeBuilder<SubscriptionReminder> builder)
    {
        builder.ToTable("subscription_reminders", AppDbContext.SaasSchema);
        builder.Property(reminder => reminder.Kind).IsRequired();
        builder.Property(reminder => reminder.DueKey).HasMaxLength(SubscriptionReminders.MaxKeyLength).IsRequired();
        builder.Property(reminder => reminder.TemplateCode).HasMaxLength(SubscriptionReminder.MaxTemplateCodeLength).IsRequired();
        builder.Property(reminder => reminder.Channels).HasMaxLength(SubscriptionReminder.MaxChannelsLength).IsRequired();
        builder.Property(reminder => reminder.SentAtUtc).IsRequired();
        builder.HasIndex(reminder => new { reminder.TenantId, reminder.DueKey }).IsUnique();
        builder.HasIndex(reminder => new { reminder.TenantId, reminder.SentAtUtc });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(reminder => reminder.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubscriptionReminderRepository(AppDbContext context) : Repository<SubscriptionReminder>(context), ISubscriptionReminderRepository
{
    public Task<bool> ExistsAsync(Guid tenantId, string dueKey, CancellationToken cancellationToken) =>
        Set.AsNoTracking().AnyAsync(reminder => reminder.TenantId == tenantId && reminder.DueKey == dueKey, cancellationToken);
}

/// <summary>
/// The Trial and Active clients with their plan fee, wallet and last reminder, one statement (scalar subqueries per client on the
/// unique wallet and plan keys). Platform tables only: no elevation, readable in any tenant scope.
/// </summary>
internal sealed class SubscriptionStateReader(AppDbContext context) : ISubscriptionStateReader
{
    public async Task<IReadOnlyList<ClientSubscriptionRow>> ListClientsAsync(CancellationToken cancellationToken) =>
        await Rows(context.Set<Tenant>().AsNoTracking().Where(tenant => tenant.Status != TenantStatus.Inactive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<ClientSubscriptionRow?> FindAsync(Guid tenantId, CancellationToken cancellationToken) =>
        TenantIds.IsPlatform(tenantId)
            ? null
            : await Rows(context.Set<Tenant>().AsNoTracking().Where(tenant => tenant.Id == tenantId))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

    private IQueryable<ClientSubscriptionRow> Rows(IQueryable<Tenant> tenants)
    {
        var platform = TenantIds.Platform;
        return tenants
            .Where(tenant => tenant.Id != platform)
            .Select(tenant => new ClientSubscriptionRow(
                tenant.Id,
                tenant.Name.En,
                tenant.Name.Ar,
                tenant.PortalSlug,
                tenant.Status,
                tenant.TrialEndsAtUtc,
                tenant.PreferredLanguage,
                tenant.ManagerName,
                tenant.ManagerMobile,
                tenant.Email,
                context.Set<Plan>().Where(plan => plan.Id == tenant.PlanId).Select(plan => plan.MonthlyFee).FirstOrDefault(),
                context.Set<Wallet>().Where(wallet => wallet.TenantId == tenant.Id).Select(wallet => (decimal?)wallet.Balance).FirstOrDefault(),
                context.Set<Wallet>().Where(wallet => wallet.TenantId == tenant.Id).Select(wallet => wallet.CurrencyCode).FirstOrDefault(),
                context.Set<Wallet>().Where(wallet => wallet.TenantId == tenant.Id).Select(wallet => wallet.AtOrBelowZeroSinceUtc).FirstOrDefault(),
                context.Set<Wallet>().Where(wallet => wallet.TenantId == tenant.Id).Select(wallet => (DateTimeOffset?)wallet.NextChargeAtUtc).FirstOrDefault(),
                context.Set<SubscriptionReminder>().Where(reminder => reminder.TenantId == tenant.Id).OrderByDescending(reminder => reminder.SentAtUtc).Select(reminder => reminder.DueKey).FirstOrDefault(),
                context.Set<SubscriptionReminder>().Where(reminder => reminder.TenantId == tenant.Id).OrderByDescending(reminder => reminder.SentAtUtc).Select(reminder => (DateTimeOffset?)reminder.SentAtUtc).FirstOrDefault()));
    }
}

/// <summary>
/// The state of one client for sign-in: its figures (<see cref="SubscriptionStateReader"/>) and the platform's grace days — the
/// platform tenant's own row of <c>subscriptionGraceDays</c>, read under <c>ForTenant(platform)</c> without elevation, else 7.
/// </summary>
internal sealed class SubscriptionStateLookup(ISubscriptionStateReader reader, AppDbContext context, ICurrentTenantSetter tenantScope, IClock clock) : ISubscriptionStateLookup
{
    public async Task<SubscriptionStatus> FindAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var row = await reader.FindAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return SubscriptionStatus.None;
        }

        var key = SettingKeys.SubscriptionGraceDays.Key;
        string? grace;
        using (tenantScope.ForTenant(TenantIds.Platform))
        {
            grace = await context.Set<TenantSetting>().AsNoTracking()
                .Where(setting => setting.TenantId == TenantIds.Platform && setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return row.StateAt(clock.UtcNow, SettingKeys.SubscriptionGraceDays.Resolve(null, grace));
    }
}
