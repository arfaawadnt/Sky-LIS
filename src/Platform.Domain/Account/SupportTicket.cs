using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Account;

/// <summary>
/// A laboratory's request for help from National Technology (SRS CP-67) and the platform desk's answer. The transition
/// table of intake LIS-INT-ACC-001 §7 lives here, one method per row: Open (S1), Reply (S2 laboratory, S3 desk), Start
/// (S4), Resolve (S5), Reopen (S6), Close (S7). A Closed ticket is frozen (BR-15: nothing is erased).
/// </summary>
/// <remarks>
/// Platform-level aggregate (<c>saas.support_tickets</c>), deliberately not <see cref="ITenantScoped"/>: the desk of the
/// platform tenant reads and answers every laboratory's tickets without elevation. <see cref="TenantId"/> is the
/// laboratory and is set once by the factory; isolation is the repository's scope plus the table's RLS policy (§5).
/// </remarks>
public sealed class SupportTicket : AggregateRoot
{
    public const int MaxNumberLength = 20;
    public const int MaxSubjectLength = 200;
    public const int MaxTextLength = 4000;
    public const int MaxNameLength = 200;
    public const int MaxReplies = 500;
    public const string NumberPrefix = "TKT";
    public const string NumberKind = "ticket";

    private readonly List<SupportTicketReply> _replies = [];

    private SupportTicket()
    {
        Number = string.Empty;
        Subject = string.Empty;
        Description = string.Empty;
        OpenedByName = string.Empty;
    }

    private SupportTicket(Guid id, Guid tenantId, string number, string subject, string description, SupportTicketPriority priority, Guid openedByUserId, string openedByName, string? laboratoryNameEn, string? laboratoryNameAr, DateTimeOffset nowUtc)
        : base(id)
    {
        TenantId = tenantId;
        Number = number;
        Subject = subject;
        Description = description;
        Priority = priority;
        Status = SupportTicketStatus.Open;
        OpenedByUserId = openedByUserId;
        OpenedByName = openedByName;
        LaboratoryNameEn = laboratoryNameEn;
        LaboratoryNameAr = laboratoryNameAr;
        OpenedAtUtc = nowUtc;
        LastActivityAtUtc = nowUtc;
    }

    /// <summary>The laboratory (tenant) the ticket belongs to; never the platform tenant (INV-SUP-001).</summary>
    public Guid TenantId { get; private set; }

    /// <summary><c>TKT-&lt;year&gt;-&lt;sequence&gt;</c> from the laboratory's counter; unique per laboratory (INV-SUP-002).</summary>
    public string Number { get; private set; }

    public string Subject { get; private set; }

    public string Description { get; private set; }

    public SupportTicketPriority Priority { get; private set; }

    public SupportTicketStatus Status { get; private set; }

    public Guid OpenedByUserId { get; private set; }

    /// <summary>The opener's display name at the time, for the desk (which cannot read the laboratory's memberships).</summary>
    public string OpenedByName { get; private set; }

    /// <summary>The laboratory's name when the ticket was opened (snapshot for the desk list; <see langword="null"/> when the client record was not available).</summary>
    public string? LaboratoryNameEn { get; private set; }

    public string? LaboratoryNameAr { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    /// <summary>The time of the latest opening, reply or transition; the registers sort on it.</summary>
    public DateTimeOffset LastActivityAtUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyList<SupportTicketReply> Replies => _replies.AsReadOnly();

    public bool IsClosed => Status == SupportTicketStatus.Closed;

    /// <summary>S1 Open (<c>support.create</c>): a laboratory raises a ticket. The platform tenant never opens one (SUP-020).</summary>
    public static SupportTicket Open(
        Guid tenantId,
        string number,
        string subject,
        string description,
        SupportTicketPriority priority,
        Guid openedByUserId,
        string? openedByName,
        string? laboratoryNameEn,
        string? laboratoryNameAr,
        DateTimeOffset nowUtc)
    {
        Require(tenantId != Guid.Empty && !TenantIds.IsPlatform(tenantId), AccountCodes.PlatformCannotOpen, "Tickets are opened by laboratories; the platform desk answers them.", DomainFault.Refused);
        Require(openedByUserId != Guid.Empty, AccountCodes.SessionActorRequired, "A ticket needs the user who opens it.", DomainFault.Validation);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number.Trim().Length, MaxNumberLength);
        Require(Enum.IsDefined(priority), AccountCodes.PriorityInvalid, "The priority is Low, Normal or High.", DomainFault.Validation);

        var ticket = new SupportTicket(
            Guid.CreateVersion7(),
            tenantId,
            number.Trim(),
            CleanText(subject, MaxSubjectLength, AccountCodes.SubjectInvalid, "The subject"),
            CleanText(description, MaxTextLength, AccountCodes.DescriptionInvalid, "The description"),
            priority,
            openedByUserId,
            CleanName(openedByName) ?? string.Empty,
            CleanName(laboratoryNameEn),
            CleanName(laboratoryNameAr),
            nowUtc);
        ticket.Raise(new SupportTicketOpened(ticket.Id, tenantId, ticket.Number, priority, openedByUserId, nowUtc));
        return ticket;
    }

    /// <summary>S2 Reply from the laboratory (<c>support.create</c>): a Resolved ticket reopens; a Closed one refuses (SUP-010).</summary>
    public SupportTicketReply ReplyFromLaboratory(Guid authorUserId, string? authorName, string text, DateTimeOffset nowUtc)
    {
        var reply = AddReply(authorUserId, authorName, text, fromPlatform: false, nowUtc);
        if (Status == SupportTicketStatus.Resolved)
        {
            Status = SupportTicketStatus.Open;
            ResolvedAtUtc = null;
            Raise(new SupportTicketReopened(Id, TenantId, Number, authorUserId, nowUtc));
        }

        return reply;
    }

    /// <summary>S3 Reply from the platform desk (<c>support.manage</c>): an Open ticket moves to InProgress; a Closed one refuses (SUP-010).</summary>
    public SupportTicketReply ReplyFromPlatform(Guid authorUserId, string? authorName, string text, DateTimeOffset nowUtc)
    {
        var reply = AddReply(authorUserId, authorName, text, fromPlatform: true, nowUtc);
        if (Status == SupportTicketStatus.Open)
        {
            Status = SupportTicketStatus.InProgress;
            Raise(new SupportTicketStarted(Id, TenantId, Number, authorUserId, nowUtc));
        }

        return reply;
    }

    /// <summary>S4 Start (<c>support.manage</c>): Open → InProgress (SUP-011 otherwise).</summary>
    public void Start(Guid actorUserId, DateTimeOffset nowUtc)
    {
        RequireNotClosed("start");
        RequireState(SupportTicketStatus.Open, "start");

        Status = SupportTicketStatus.InProgress;
        LastActivityAtUtc = nowUtc;
        Raise(new SupportTicketStarted(Id, TenantId, Number, actorUserId, nowUtc));
    }

    /// <summary>S5 Resolve (<c>support.manage</c>): Open or InProgress → Resolved; an optional note is kept as a desk reply.</summary>
    public void Resolve(Guid actorUserId, string? actorName, string? note, DateTimeOffset nowUtc)
    {
        RequireNotClosed("resolve");
        if (Status is not (SupportTicketStatus.Open or SupportTicketStatus.InProgress))
        {
            throw new InvalidStateTransitionException(AccountCodes.TransitionNotAllowed, "support ticket", Status.ToString(), "resolve");
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            AddReply(actorUserId, actorName, note, fromPlatform: true, nowUtc);
        }

        Status = SupportTicketStatus.Resolved;
        ResolvedAtUtc = nowUtc;
        LastActivityAtUtc = nowUtc;
        Raise(new SupportTicketResolved(Id, TenantId, Number, actorUserId, nowUtc));
    }

    /// <summary>S6 Reopen (<c>support.create</c>): Resolved → Open (SUP-011 otherwise).</summary>
    public void Reopen(Guid actorUserId, DateTimeOffset nowUtc)
    {
        RequireNotClosed("reopen");
        RequireState(SupportTicketStatus.Resolved, "reopen");

        Status = SupportTicketStatus.Open;
        ResolvedAtUtc = null;
        LastActivityAtUtc = nowUtc;
        Raise(new SupportTicketReopened(Id, TenantId, Number, actorUserId, nowUtc));
    }

    /// <summary>S7 Close (<c>support.create</c> for the laboratory, <c>support.manage</c> for the desk): any live state → Closed. Terminal.</summary>
    public void Close(Guid actorUserId, bool byPlatform, DateTimeOffset nowUtc)
    {
        RequireNotClosed("close");

        Status = SupportTicketStatus.Closed;
        ClosedAtUtc = nowUtc;
        ClosedByUserId = actorUserId;
        LastActivityAtUtc = nowUtc;
        Raise(new SupportTicketClosed(Id, TenantId, Number, actorUserId, byPlatform, nowUtc));
    }

    private SupportTicketReply AddReply(Guid authorUserId, string? authorName, string text, bool fromPlatform, DateTimeOffset nowUtc)
    {
        RequireNotClosed("reply");
        Require(authorUserId != Guid.Empty, AccountCodes.SessionActorRequired, "A reply needs its author.", DomainFault.Validation);
        Require(_replies.Count < MaxReplies, AccountCodes.TooManyReplies, $"A ticket holds at most {MaxReplies} replies; open a new ticket.", DomainFault.Conflict);

        var reply = SupportTicketReply.Create(authorUserId, CleanName(authorName) ?? string.Empty, CleanText(text, MaxTextLength, AccountCodes.ReplyInvalid, "The reply"), fromPlatform, nowUtc);
        _replies.Add(reply);
        LastActivityAtUtc = nowUtc;
        Raise(new SupportTicketReplied(Id, TenantId, Number, reply.Id, fromPlatform, authorUserId, nowUtc));
        return reply;
    }

    private void RequireNotClosed(string action)
    {
        if (Status == SupportTicketStatus.Closed)
        {
            throw new InvalidStateTransitionException(AccountCodes.TicketClosed, "support ticket", Status.ToString(), action);
        }
    }

    private void RequireState(SupportTicketStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidStateTransitionException(AccountCodes.TransitionNotAllowed, "support ticket", Status.ToString(), action);
        }
    }

    private static string CleanText(string? value, int maxLength, string code, string what)
    {
        var cleaned = (value ?? string.Empty).Trim();
        Require(cleaned.Length is >= 1 && cleaned.Length <= maxLength, code, $"{what} needs 1 to {maxLength} characters.", DomainFault.Validation);
        return cleaned;
    }

    private static string? CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        return cleaned.Length <= MaxNameLength ? cleaned : cleaned[..MaxNameLength];
    }
}

/// <summary>One message on a ticket, from the laboratory or from the platform desk. Immutable once written.</summary>
public sealed class SupportTicketReply : Entity
{
    private SupportTicketReply()
    {
        AuthorName = string.Empty;
        Text = string.Empty;
    }

    private SupportTicketReply(Guid id, Guid authorUserId, string authorName, string text, bool fromPlatform, DateTimeOffset atUtc)
        : base(id)
    {
        AuthorUserId = authorUserId;
        AuthorName = authorName;
        Text = text;
        FromPlatform = fromPlatform;
        AtUtc = atUtc;
    }

    public Guid AuthorUserId { get; private set; }

    public string AuthorName { get; private set; }

    public string Text { get; private set; }

    /// <summary>True when National Technology's desk wrote it.</summary>
    public bool FromPlatform { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    internal static SupportTicketReply Create(Guid authorUserId, string authorName, string text, bool fromPlatform, DateTimeOffset atUtc) =>
        new(Guid.CreateVersion7(), authorUserId, authorName, text, fromPlatform, atUtc);
}
