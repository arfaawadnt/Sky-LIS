using Platform.Domain.Account;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Account;

/// <summary>Acceptance scenarios of intake LIS-INT-ACC-001 §16 (S-S1…S-S7): one test per transition row, per refused precondition and per invariant.</summary>
public sealed class SupportTicketTests
{
    private static readonly Guid Laboratory = Guid.CreateVersion7();
    private static readonly Guid LabUser = Guid.CreateVersion7();
    private static readonly Guid DeskUser = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static SupportTicket Opened() =>
        SupportTicket.Open(Laboratory, "TKT-2026-00001", " Printer labels cut ", " The barcode labels print cut off. ", SupportTicketPriority.High, LabUser, "Lab Admin", "Al Noor Labs", "مختبرات النور", Now);

    [Fact]
    public void S1_Open_happy_path()
    {
        var ticket = Opened();

        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Equal((Laboratory, "TKT-2026-00001", "Printer labels cut", "The barcode labels print cut off."), (ticket.TenantId, ticket.Number, ticket.Subject, ticket.Description));
        Assert.Equal((SupportTicketPriority.High, LabUser, "Lab Admin"), (ticket.Priority, ticket.OpenedByUserId, ticket.OpenedByName));
        Assert.Equal(("Al Noor Labs", "مختبرات النور"), (ticket.LaboratoryNameEn, ticket.LaboratoryNameAr));
        Assert.Equal((Now, Now), (ticket.OpenedAtUtc, ticket.LastActivityAtUtc));
        Assert.Empty(ticket.Replies);
        var raised = Assert.IsType<SupportTicketOpened>(Assert.Single(ticket.DomainEvents));
        Assert.Equal((ticket.Id, Laboratory, "TKT-2026-00001", SupportTicketPriority.High), (raised.TicketId, raised.TenantId, raised.Number, raised.Priority));
    }

    [Fact]
    public void S1_Open_refused_for_the_platform_tenant_SUP_020()
    {
        var refusal = Assert.Throws<DomainException>(() =>
            SupportTicket.Open(TenantIds.Platform, "TKT-2026-00001", "Subject", "Description", SupportTicketPriority.Normal, DeskUser, "Desk", null, null, Now));

        Assert.Equal((AccountCodes.PlatformCannotOpen, DomainFault.Refused), (refusal.Code, refusal.Fault));
    }

    [Theory]
    [InlineData("", "Description", AccountCodes.SubjectInvalid)]
    [InlineData("   ", "Description", AccountCodes.SubjectInvalid)]
    [InlineData("Subject", "", AccountCodes.DescriptionInvalid)]
    public void S1_Open_refuses_an_empty_subject_or_description(string subject, string description, string code)
    {
        var refusal = Assert.Throws<DomainException>(() =>
            SupportTicket.Open(Laboratory, "TKT-2026-00001", subject, description, SupportTicketPriority.Low, LabUser, "Lab", null, null, Now));

        Assert.Equal((code, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void S1_Open_refuses_a_subject_longer_than_200_characters_and_an_unknown_priority()
    {
        Assert.Equal(AccountCodes.SubjectInvalid, Assert.Throws<DomainException>(() =>
            SupportTicket.Open(Laboratory, "TKT-2026-00001", new string('s', SupportTicket.MaxSubjectLength + 1), "Description", SupportTicketPriority.Low, LabUser, "Lab", null, null, Now)).Code);
        Assert.Equal(AccountCodes.PriorityInvalid, Assert.Throws<DomainException>(() =>
            SupportTicket.Open(Laboratory, "TKT-2026-00001", "Subject", "Description", (SupportTicketPriority)9, LabUser, "Lab", null, null, Now)).Code);
    }

    [Fact]
    public void S2_A_laboratory_reply_is_recorded_and_keeps_an_open_ticket_open()
    {
        var ticket = Opened();
        ticket.ClearDomainEvents();

        var reply = ticket.ReplyFromLaboratory(LabUser, "Lab Admin", " Any news? ", Now.AddHours(1));

        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Equal(("Any news?", false, Now.AddHours(1)), (reply.Text, reply.FromPlatform, reply.AtUtc));
        Assert.Equal(Now.AddHours(1), ticket.LastActivityAtUtc);
        var raised = Assert.IsType<SupportTicketReplied>(Assert.Single(ticket.DomainEvents));
        Assert.False(raised.FromPlatform);
        Assert.Equal(reply.Id, raised.ReplyId);
    }

    [Fact]
    public void S2_A_laboratory_reply_reopens_a_resolved_ticket()
    {
        var ticket = Opened();
        ticket.Resolve(DeskUser, "Desk", null, Now);
        ticket.ClearDomainEvents();

        ticket.ReplyFromLaboratory(LabUser, "Lab Admin", "Still broken", Now.AddMinutes(5));

        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolvedAtUtc);
        Assert.Collection(ticket.DomainEvents, first => Assert.IsType<SupportTicketReplied>(first), second => Assert.IsType<SupportTicketReopened>(second));
    }

    [Fact]
    public void S2_A_closed_ticket_refuses_replies_SUP_010()
    {
        var ticket = Opened();
        ticket.Close(LabUser, byPlatform: false, Now);

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => ticket.ReplyFromLaboratory(LabUser, "Lab", "late", Now));

        Assert.Equal(AccountCodes.TicketClosed, refusal.Code);
        Assert.Empty(ticket.Replies);
    }

    [Fact]
    public void S2_An_empty_reply_is_refused_SUP_003()
    {
        var refusal = Assert.Throws<DomainException>(() => Opened().ReplyFromLaboratory(LabUser, "Lab", "  ", Now));

        Assert.Equal((AccountCodes.ReplyInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void INV_SUP_005_A_ticket_holds_at_most_500_replies()
    {
        var ticket = Opened();
        for (var index = 0; index < SupportTicket.MaxReplies; index++)
        {
            ticket.ReplyFromLaboratory(LabUser, "Lab", $"message {index}", Now);
        }

        var refusal = Assert.Throws<DomainException>(() => ticket.ReplyFromLaboratory(LabUser, "Lab", "one too many", Now));

        Assert.Equal((AccountCodes.TooManyReplies, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Equal(SupportTicket.MaxReplies, ticket.Replies.Count);
    }

    [Fact]
    public void S3_A_desk_reply_starts_an_open_ticket_and_is_marked_from_the_platform()
    {
        var ticket = Opened();
        ticket.ClearDomainEvents();

        var reply = ticket.ReplyFromPlatform(DeskUser, "NT Support", "We are on it.", Now.AddMinutes(10));

        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.True(reply.FromPlatform);
        Assert.Equal("NT Support", reply.AuthorName);
        Assert.Collection(ticket.DomainEvents, first => Assert.True(Assert.IsType<SupportTicketReplied>(first).FromPlatform), second => Assert.IsType<SupportTicketStarted>(second));
    }

    [Fact]
    public void S3_A_desk_reply_on_a_resolved_ticket_keeps_it_resolved()
    {
        var ticket = Opened();
        ticket.Resolve(DeskUser, "Desk", null, Now);

        ticket.ReplyFromPlatform(DeskUser, "Desk", "Follow-up note", Now);

        Assert.Equal(SupportTicketStatus.Resolved, ticket.Status);
    }

    [Fact]
    public void S4_Start_moves_an_open_ticket_in_progress_and_refuses_otherwise_SUP_011()
    {
        var ticket = Opened();
        ticket.ClearDomainEvents();

        ticket.Start(DeskUser, Now);

        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.IsType<SupportTicketStarted>(Assert.Single(ticket.DomainEvents));
        Assert.Equal(AccountCodes.TransitionNotAllowed, Assert.Throws<InvalidStateTransitionException>(() => ticket.Start(DeskUser, Now)).Code);
    }

    [Fact]
    public void S5_Resolve_records_the_time_and_keeps_the_note_as_a_desk_reply()
    {
        var ticket = Opened();
        ticket.Start(DeskUser, Now);
        ticket.ClearDomainEvents();

        ticket.Resolve(DeskUser, "NT Support", " Driver updated. ", Now.AddHours(2));

        Assert.Equal(SupportTicketStatus.Resolved, ticket.Status);
        Assert.Equal(Now.AddHours(2), ticket.ResolvedAtUtc);
        var note = Assert.Single(ticket.Replies);
        Assert.Equal(("Driver updated.", true), (note.Text, note.FromPlatform));
        Assert.IsType<SupportTicketResolved>(ticket.DomainEvents.Last());
    }

    [Fact]
    public void S5_Resolve_refuses_a_resolved_or_closed_ticket()
    {
        var resolved = Opened();
        resolved.Resolve(DeskUser, "Desk", null, Now);
        Assert.Equal(AccountCodes.TransitionNotAllowed, Assert.Throws<InvalidStateTransitionException>(() => resolved.Resolve(DeskUser, "Desk", null, Now)).Code);

        var closed = Opened();
        closed.Close(DeskUser, byPlatform: true, Now);
        Assert.Equal(AccountCodes.TicketClosed, Assert.Throws<InvalidStateTransitionException>(() => closed.Resolve(DeskUser, "Desk", "note", Now)).Code);
        Assert.Empty(closed.Replies);
    }

    [Fact]
    public void S6_Reopen_brings_a_resolved_ticket_back_and_refuses_an_open_one_SUP_011()
    {
        var ticket = Opened();
        Assert.Equal(AccountCodes.TransitionNotAllowed, Assert.Throws<InvalidStateTransitionException>(() => ticket.Reopen(LabUser, Now)).Code);

        ticket.Resolve(DeskUser, "Desk", null, Now);
        ticket.ClearDomainEvents();
        ticket.Reopen(LabUser, Now.AddDays(1));

        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolvedAtUtc);
        Assert.IsType<SupportTicketReopened>(Assert.Single(ticket.DomainEvents));
    }

    [Fact]
    public void S7_Close_is_terminal_and_records_who_closed()
    {
        var ticket = Opened();
        ticket.ClearDomainEvents();

        ticket.Close(DeskUser, byPlatform: true, Now.AddDays(3));

        Assert.True(ticket.IsClosed);
        Assert.Equal((Now.AddDays(3), DeskUser), (ticket.ClosedAtUtc, ticket.ClosedByUserId));
        Assert.True(Assert.IsType<SupportTicketClosed>(Assert.Single(ticket.DomainEvents)).ByPlatform);
        Assert.Equal(AccountCodes.TicketClosed, Assert.Throws<InvalidStateTransitionException>(() => ticket.Close(LabUser, byPlatform: false, Now)).Code);
        Assert.Equal(AccountCodes.TicketClosed, Assert.Throws<InvalidStateTransitionException>(() => ticket.Reopen(LabUser, Now)).Code);
        Assert.Equal(AccountCodes.TicketClosed, Assert.Throws<InvalidStateTransitionException>(() => ticket.Start(DeskUser, Now)).Code);
    }
}
