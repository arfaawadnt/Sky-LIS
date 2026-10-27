using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Account;
using Platform.Application.Audit;
using Platform.Application.Catalogue;
using Platform.Application.Identity;
using Platform.Application.Tenancy;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;
using Platform.Domain.Account;
using Platform.Domain.Catalogue;
using Platform.Domain.Identity;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-ACC-001: support tickets are isolated per laboratory by the repository and by the
/// table's RLS policy while the platform desk sees all (INV-SUP-004), numbers are unique per laboratory, replies persist
/// with their ticket; the read adapters of the dashboard, profile and audit export.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_laboratory_sees_only_its_own_tickets_and_the_platform_desk_sees_every_laboratory()
    {
        var (labA, labB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var user = await RegisterUserAsync();
        var ticketA = await OpenTicketAsync(labA, user, "Printer");
        var ticketB = await OpenTicketAsync(labB, user, "Analyser");

        using (var scopeB = fixture.AppScope(labB, user))
        {
            var repository = scopeB.ServiceProvider.GetRequiredService<ISupportTicketRepository>();
            Assert.Null(await repository.GetForUpdateAsync(ticketA, CancellationToken.None));
            Assert.Null(await repository.GetDetailAsync(ticketA, CancellationToken.None));
            var page = await repository.ListAsync(new SupportTicketFilter(null, null, null, null, null), PageRequest.Normalized(1, 50), CancellationToken.None);
            Assert.Equal([ticketB], page.Items.Select(item => item.Id).ToArray());

            // The table's own policy, without the repository's scope: laboratory B's connection reaches no row of A.
            var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await context.Set<SupportTicket>().CountAsync(ticket => ticket.Id == ticketA));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM saas.support_ticket_replies r JOIN saas.support_tickets t ON t.id = r.support_ticket_id WHERE t.tenant_id = {0}", labA).SingleAsync());
        }

        using var desk = fixture.AppScope(TenantIds.Platform, user);
        var deskTickets = desk.ServiceProvider.GetRequiredService<ISupportTicketRepository>();
        var all = await deskTickets.ListAsync(new SupportTicketFilter(null, null, null, null, null), PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Contains(all.Items, item => item.Id == ticketA);
        Assert.Contains(all.Items, item => item.Id == ticketB);
        var onlyA = await deskTickets.ListAsync(new SupportTicketFilter(null, null, null, null, labA), PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal([ticketA], onlyA.Items.Select(item => item.Id).ToArray());

        // The desk answers laboratory A's ticket from the platform scope: the write passes the policy's WITH CHECK.
        var ticket = (await deskTickets.GetForUpdateAsync(ticketA, CancellationToken.None))!;
        ticket.ReplyFromPlatform(user, "NT Support", "Looking into it.", Now.AddMinutes(5));
        await desk.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        using var labScope = fixture.AppScope(labA, user);
        var detail = (await labScope.ServiceProvider.GetRequiredService<ISupportTicketRepository>().GetDetailAsync(ticketA, CancellationToken.None))!;
        Assert.Equal(("InProgress", "Looking into it.", true), (detail.Status, Assert.Single(detail.Replies).Text, detail.Replies[0].FromPlatform));
    }

    [DatabaseFact]
    public async Task An_unresolved_tenant_sees_no_ticket()
    {
        var user = await RegisterUserAsync();
        var ticketId = await OpenTicketAsync(Guid.CreateVersion7(), user, "Hidden");

        using var scope = fixture.AppScope(null, user);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ISupportTicketRepository>().GetForUpdateAsync(ticketId, CancellationToken.None));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<SupportTicket>().CountAsync(ticket => ticket.Id == ticketId));
    }

    [DatabaseFact]
    public async Task Ticket_numbers_are_unique_per_laboratory_and_replies_persist_in_order()
    {
        var lab = Guid.CreateVersion7();
        var user = await RegisterUserAsync();
        var first = await OpenTicketAsync(lab, user, "First", "TKT-2026-99999");

        var duplicate = await Assert.ThrowsAsync<DomainException>(() => OpenTicketAsync(lab, user, "Duplicate", "TKT-2026-99999"));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicate.Code);
        await OpenTicketAsync(Guid.CreateVersion7(), user, "Other lab", "TKT-2026-99999");   // the same number in another laboratory is fine

        using (var scope = fixture.AppScope(lab, user))
        {
            var ticket = (await scope.ServiceProvider.GetRequiredService<ISupportTicketRepository>().GetForUpdateAsync(first, CancellationToken.None))!;
            ticket.ReplyFromLaboratory(user, "Lab Admin", "one", Now.AddMinutes(1));
            ticket.ReplyFromLaboratory(user, "Lab Admin", "two", Now.AddMinutes(2));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(lab, user);
        var repository = reader.ServiceProvider.GetRequiredService<ISupportTicketRepository>();
        var detail = (await repository.GetDetailAsync(first, CancellationToken.None))!;
        Assert.Equal(["one", "two"], detail.Replies.Select(reply => reply.Text).ToArray());
        Assert.StartsWith("TKT-2026-", detail.Number, StringComparison.Ordinal);
        var item = Assert.Single((await repository.ListAsync(new SupportTicketFilter(SupportTicketStatus.Open, null, "fir", user, null), PageRequest.Normalized(1, 10), CancellationToken.None)).Items);
        Assert.Equal((2, false), (item.ReplyCount, item.LastReplyFromPlatform));
    }

    [DatabaseFact]
    public async Task The_runtime_role_has_no_delete_on_tickets_and_both_tables_force_their_policy()
    {
        using var scope = fixture.AppScope(Guid.CreateVersion7());
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var forced = await context.Database.SqlQueryRaw<bool>("SELECT bool_and(relrowsecurity AND relforcerowsecurity) AS \"Value\" FROM pg_class WHERE oid IN ('saas.support_tickets'::regclass, 'saas.support_ticket_replies'::regclass)").SingleAsync();
        Assert.True(forced);
        var canDelete = await context.Database.SqlQueryRaw<bool>("SELECT has_table_privilege(current_user, 'saas.support_tickets', 'DELETE') AS \"Value\"").SingleAsync();
        Assert.False(canDelete);
    }

    [DatabaseFact]
    public async Task The_dashboard_reader_answers_null_without_the_wave_two_tables_and_zero_for_an_empty_branch()
    {
        using var scope = fixture.AppScope(Guid.CreateVersion7());
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visitsExist = await context.Database.SqlQueryRaw<bool>("SELECT to_regclass('app.visits') IS NOT NULL AS \"Value\"").SingleAsync();
        var samplesExist = await context.Database.SqlQueryRaw<bool>("SELECT to_regclass('app.samples') IS NOT NULL AS \"Value\"").SingleAsync();
        var paymentsExist = await context.Database.SqlQueryRaw<bool>("SELECT to_regclass('app.payments') IS NOT NULL AS \"Value\"").SingleAsync();

        var window = new DashboardWindow(Now, Now.AddDays(1), Now.AddDays(-6), 120);
        var figures = await scope.ServiceProvider.GetRequiredService<IBranchDashboardReader>().ReadAsync(Guid.CreateVersion7(), window, CancellationToken.None);

        Assert.Equal(visitsExist ? 0 : (int?)null, figures.Registrations);
        Assert.Equal(visitsExist ? 0 : (int?)null, figures.RegistrationsByLocalDay?.Count);
        Assert.Equal(samplesExist && visitsExist ? 0 : (int?)null, figures.SamplesReceived);
        Assert.Equal(samplesExist && visitsExist ? 0 : (int?)null, figures.RejectedSamples);
        Assert.Equal(paymentsExist && visitsExist ? 0m : (decimal?)null, figures.CollectedToday);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IBranchDashboardReader>().ReadWalletAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_directory_returns_the_own_account_with_its_membership_and_the_members_of_the_tenant_only()
    {
        var (tenant, other) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var user = await RegisterUserAsync("Mona Adel");
        var colleague = await RegisterUserAsync("Omar Samir");
        await AddMembershipAsync(tenant, user, "M-001", "ar");
        await AddMembershipAsync(tenant, colleague, "M-002", "en");
        await AddMembershipAsync(other, colleague, "M-003", "en");

        using var scope = fixture.AppScope(tenant, user);
        var directory = scope.ServiceProvider.GetRequiredService<IAccountDirectory>();
        var own = (await directory.GetOwnAsync(user, CancellationToken.None))!;
        Assert.Equal(("Mona Adel", "M-001", "ar"), (own.DisplayName, own.EmployeeCode, own.PreferredLanguage));
        Assert.Null(own.RoleNameEn);

        var members = await directory.ListMembersAsync(CancellationToken.None);
        Assert.Equal(["Mona Adel", "Omar Samir"], members.Select(member => member.DisplayName).ToArray());
        var profiles = scope.ServiceProvider.GetRequiredService<ILaboratoryProfileLookup>();
        Assert.Equal(TestTenants.LaboratoryNamePrefix + tenant.ToString("N")[..8], (await profiles.FindAsync(tenant, CancellationToken.None))?.NameEn);   // the fixture's placeholder record
        Assert.Null(await profiles.FindAsync(TenantIds.Platform, CancellationToken.None));   // the platform's registry row is no laboratory (DbReviewTenantKeys)
    }

    [DatabaseFact]
    public async Task The_audit_export_reader_returns_the_newest_field_changes_and_events_of_the_tenant()
    {
        var tenant = Guid.CreateVersion7();
        var user = await RegisterUserAsync("Auditor");
        Guid groupId;
        using (var scope = fixture.AppScope(tenant, user, "Auditor"))
        {
            var group = ClinicalGroup.Create("EXP" + Guid.CreateVersion7().ToString("N")[..6], "E", new LocalizedText("Export group", "مجموعة"), null, null, 1, Now);
            scope.ServiceProvider.GetRequiredService<IClinicalGroupRepository>().Add(group);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            groupId = group.Id;
        }

        using var reader = fixture.AppScope(tenant, user);
        var rows = await reader.ServiceProvider.GetRequiredService<IAuditExportReader>().ReadAsync(new AuditEntryFilter(null, null, null, "ClinicalGroup", groupId.ToString()), 100, CancellationToken.None);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Kind == AuditEntryItem.KindFieldChange && row.Action == "Created");
        Assert.Contains(rows, row => row.Kind == AuditEntryItem.KindEvent && row.Action == "ClinicalGroupCreated" && row.ActorName == "Auditor");

        using var stranger = fixture.AppScope(Guid.CreateVersion7(), user);
        Assert.Empty(await stranger.ServiceProvider.GetRequiredService<IAuditExportReader>().ReadAsync(new AuditEntryFilter(null, null, null, null, groupId.ToString()), 100, CancellationToken.None));
    }

    private async Task<Guid> RegisterUserAsync(string displayName = "Support Tester")
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"acc.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task AddMembershipAsync(Guid tenantId, Guid userId, string employeeCode, string language)
    {
        using var scope = fixture.AppScope(tenantId, userId);
        var membership = TenantMembership.Create(tenantId, userId, employeeCode, new LocalizedText("Member " + employeeCode, "عضو"), language, Now);
        scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().Add(membership);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> OpenTicketAsync(Guid tenantId, Guid userId, string subject, string? number = null)
    {
        using var scope = fixture.AppScope(tenantId, userId);
        var ticketNumber = number ?? await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(SupportTicket.NumberKind, SupportTicket.NumberPrefix, Now, CancellationToken.None);
        var ticket = SupportTicket.Open(tenantId, ticketNumber, subject, "Details of " + subject, SupportTicketPriority.Normal, userId, "Lab Admin", null, null, Now);
        scope.ServiceProvider.GetRequiredService<ISupportTicketRepository>().Add(ticket);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return ticket.Id;
    }
}
