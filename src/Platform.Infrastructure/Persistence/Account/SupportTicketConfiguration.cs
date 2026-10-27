using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Account;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence.Account;

/// <summary>
/// <c>saas.support_tickets</c> + <c>saas.support_ticket_replies</c> (intake LIS-INT-ACC-001 §5): a platform-level table
/// keyed by <c>id</c> with an explicit <c>tenant_id</c> (the laboratory), no tenant query filter (the desk reads every
/// laboratory), isolation by the repository's scope and by the hand-written RLS policy of <c>Account.migration.sql</c>.
/// The ticket number is unique per laboratory (INV-SUP-002). Replies are owned: they live and die with the ticket.
/// </summary>
internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets", AppDbContext.SaasSchema);
        builder.HasKey(ticket => new { ticket.TenantId, ticket.Id });   // database team review 2026-10-26, comment 4: (tenant_id, id)
        builder.Property(ticket => ticket.Id).ValueGeneratedNever();

        builder.Property(ticket => ticket.TenantId).IsRequired();
        builder.Property(ticket => ticket.Number).HasMaxLength(SupportTicket.MaxNumberLength).IsRequired();
        builder.HasIndex(ticket => new { ticket.TenantId, ticket.Number }).IsUnique();

        builder.Property(ticket => ticket.Subject).HasMaxLength(SupportTicket.MaxSubjectLength).IsRequired();
        builder.Property(ticket => ticket.Description).HasMaxLength(SupportTicket.MaxTextLength).IsRequired();
        builder.Property(ticket => ticket.Priority).IsRequired();
        builder.Property(ticket => ticket.Status).IsRequired();
        builder.Property(ticket => ticket.OpenedByName).HasMaxLength(SupportTicket.MaxNameLength).IsRequired();
        builder.Property(ticket => ticket.LaboratoryNameEn).HasMaxLength(SupportTicket.MaxNameLength);
        builder.Property(ticket => ticket.LaboratoryNameAr).HasMaxLength(SupportTicket.MaxNameLength);
        builder.Property(ticket => ticket.OpenedAtUtc).IsRequired();
        builder.Property(ticket => ticket.LastActivityAtUtc).IsRequired();

        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(ticket => ticket.OpenedByUserId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_support_tickets_opened_by_user");

        builder.HasIndex(ticket => new { ticket.TenantId, ticket.LastActivityAtUtc }).HasDatabaseName("ix_support_tickets_tenant_activity");
        builder.HasIndex(ticket => new { ticket.Status, ticket.LastActivityAtUtc }).HasDatabaseName("ix_support_tickets_status_activity");

        builder.OwnsMany(ticket => ticket.Replies, reply =>
        {
            reply.ToTable("support_ticket_replies", AppDbContext.SaasSchema);
            reply.WithOwner().HasForeignKey("TenantId", "SupportTicketId");   // the ticket's tenant, filled by EF from the owner
            reply.Metadata.SetConstraintName("fk_support_ticket_replies_ticket");
            reply.Property(value => value.Id).ValueGeneratedNever();
            reply.HasKey("TenantId", nameof(SupportTicketReply.Id));
            reply.Property(value => value.AuthorUserId).IsRequired();
            reply.Property(value => value.AuthorName).HasMaxLength(SupportTicket.MaxNameLength).IsRequired();
            reply.Property(value => value.Text).HasMaxLength(SupportTicket.MaxTextLength).IsRequired();
            reply.Property(value => value.FromPlatform).IsRequired();
            reply.Property(value => value.AtUtc).IsRequired();
            reply.HasIndex("TenantId", "SupportTicketId", nameof(SupportTicketReply.AtUtc)).HasDatabaseName("ix_support_ticket_replies_ticket_at");
        });
        builder.Navigation(ticket => ticket.Replies).HasField("_replies").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(ticket => ticket.IsClosed);
    }
}
