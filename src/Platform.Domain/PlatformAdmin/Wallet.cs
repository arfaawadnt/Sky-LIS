using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// The prepaid credit of one client (SA-05, CP-66; OQ-02 "prepaid credit debited by subscription and metered usage").
/// One wallet per tenant (INV-PLT-030). Every movement is a <see cref="WalletTransaction"/> carrying the balance after
/// it; the balance on the root is what decides. Metered usage is refused once the credit does not cover it
/// (<c>WAL-010</c>, "zero balance pauses metered services"); the monthly subscription may take the balance below
/// zero (the portal keeps working, CP-66). The transaction history is never loaded with the root: new movements are
/// added to the collection and inserted, old ones are read through projections.
/// </summary>
public sealed class Wallet : AggregateRoot
{
    public const int MaxReferenceLength = 100;
    public const int MaxNoteLength = 500;

    private readonly List<WalletTransaction> _transactions = [];
    private readonly List<WalletTopUpRequest> _topUpRequests = [];

    private Wallet()
    {
        CurrencyCode = string.Empty;
    }

    private Wallet(Guid id, Guid tenantId, string currencyCode, DateTimeOffset nextChargeAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        CurrencyCode = currencyCode;
        NextChargeAtUtc = nextChargeAtUtc;
    }

    /// <summary>The client this wallet belongs to (<c>saas.tenants.id</c>); unique.</summary>
    public Guid TenantId { get; private set; }

    public decimal Balance { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>When the next monthly subscription charge falls due (end of trial for a new subscription).</summary>
    public DateTimeOffset NextChargeAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Movements staged since the wallet was loaded (the history is read through projections).</summary>
    public IReadOnlyList<WalletTransaction> Transactions => _transactions.AsReadOnly();

    /// <summary>Requests touched since the wallet was loaded, or loaded explicitly by the repository for a decision.</summary>
    public IReadOnlyList<WalletTopUpRequest> TopUpRequests => _topUpRequests.AsReadOnly();

    public bool IsAtOrBelowZero => Balance <= 0;

    /// <summary>Opens the empty wallet of a new client; the first charge falls due at <paramref name="firstChargeAtUtc"/>.</summary>
    public static Wallet Open(Guid tenantId, string currencyCode, DateTimeOffset firstChargeAtUtc)
    {
        Require(tenantId != Guid.Empty, PlatformAdminCodes.WalletNotFound, "A wallet belongs to a client.", DomainFault.Validation);
        var currency = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
        Require(currency.Length == 3 && currency.All(char.IsAsciiLetterUpper), PlatformAdminCodes.CurrencyInvalid, "The wallet currency is three letters.", DomainFault.Validation);
        return new Wallet(Guid.CreateVersion7(), tenantId, currency, firstChargeAtUtc);
    }

    /// <summary>W1 Top up (<c>tenancy.manage</c>): credits the amount with a payment reference; completes the laboratory's pending request when one is given.</summary>
    public WalletTransaction TopUp(decimal amount, string reference, string? note, Guid? actorUserId, DateTimeOffset nowUtc, WalletTopUpRequest? fulfils = null)
    {
        var credited = DomainText.EnteredMoney(amount, PlatformAdminCodes.AmountInvalid, "The top-up amount");
        var cleanedReference = DomainText.Required(reference, MaxReferenceLength, PlatformAdminCodes.ReferenceRequired, "The payment reference");
        if (fulfils is not null)
        {
            Require(_topUpRequests.Contains(fulfils), PlatformAdminCodes.TopUpRequestNotFound, "The request does not belong to this wallet.", DomainFault.NotFound);
        }

        Balance += credited;
        var transaction = WalletTransaction.Record(Id, WalletTransactionKind.TopUp, credited, Balance, cleanedReference, Note(note), actorUserId, nowUtc);
        _transactions.Add(transaction);
        fulfils?.Complete(transaction.Id, nowUtc);
        Raise(new WalletToppedUp(Id, TenantId, transaction.Id, credited, cleanedReference, nowUtc));
        return transaction;
    }

    /// <summary>W2 Monthly subscription charge (system): debits the plan fee even below zero and moves the next charge one month on.</summary>
    public WalletTransaction ChargeSubscription(decimal monthlyFee, string reference, DateTimeOffset nowUtc)
    {
        var debit = DomainText.Money(monthlyFee, PlatformAdminCodes.AmountInvalid, "The monthly fee", allowZero: true);
        Balance -= debit;
        var transaction = WalletTransaction.Record(Id, WalletTransactionKind.Subscription, -debit, Balance, DomainText.Required(reference, MaxReferenceLength, PlatformAdminCodes.ReferenceRequired, "The charge reference"), null, null, nowUtc);
        _transactions.Add(transaction);
        NextChargeAtUtc = NextChargeAtUtc.AddMonths(1);
        Raise(new WalletDebited(Id, TenantId, transaction.Id, transaction.Kind.ToString(), debit, Balance, nowUtc));
        return transaction;
    }

    /// <summary>W3 Metered usage (system, e.g. an SMS): refused when the credit does not cover it (WAL-010).</summary>
    public WalletTransaction DebitUsage(decimal amount, string reference, DateTimeOffset nowUtc)
    {
        var debit = DomainText.Money(amount, PlatformAdminCodes.AmountInvalid, "The usage amount", allowZero: false);
        Require(Balance >= debit, PlatformAdminCodes.InsufficientBalance, "The wallet balance does not cover this usage; metered services pause at zero.", DomainFault.Conflict);
        Balance -= debit;
        var transaction = WalletTransaction.Record(Id, WalletTransactionKind.Usage, -debit, Balance, DomainText.Required(reference, MaxReferenceLength, PlatformAdminCodes.ReferenceRequired, "The usage reference"), null, null, nowUtc);
        _transactions.Add(transaction);
        Raise(new WalletDebited(Id, TenantId, transaction.Id, transaction.Kind.ToString(), debit, Balance, nowUtc));
        return transaction;
    }

    /// <summary>W4 Request a top-up (<c>wallet.create</c>, CP-66): the laboratory asks for credit; the platform tops up against it.</summary>
    public WalletTopUpRequest RequestTopUp(decimal amount, string? note, Guid requestedByUserId, DateTimeOffset nowUtc)
    {
        var requested = DomainText.EnteredMoney(amount, PlatformAdminCodes.AmountInvalid, "The requested amount");
        var request = WalletTopUpRequest.Create(Id, requested, Note(note), requestedByUserId, nowUtc);
        _topUpRequests.Add(request);
        Raise(new WalletTopUpRequested(Id, TenantId, request.Id, requested, nowUtc));
        return request;
    }

    /// <summary>W5 Decline a pending request (<c>tenancy.manage</c>).</summary>
    public void DeclineTopUp(WalletTopUpRequest request, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(_topUpRequests.Contains(request), PlatformAdminCodes.TopUpRequestNotFound, "The request does not belong to this wallet.", DomainFault.NotFound);
        request.Decline(nowUtc);
    }

    /// <summary>Hands a loaded pending request to the root so a decision goes through it (repository use only).</summary>
    public void Attach(WalletTopUpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.WalletId == Id, PlatformAdminCodes.TopUpRequestNotFound, "The request does not belong to this wallet.", DomainFault.NotFound);
        if (!_topUpRequests.Contains(request))
        {
            _topUpRequests.Add(request);
        }
    }

    private static string? Note(string? note) => DomainText.Optional(note, MaxNoteLength, PlatformAdminCodes.WalletNoteInvalid, "The note");
}

/// <summary>One movement of a wallet: positive credits, negative debits, with the balance after it.</summary>
public sealed class WalletTransaction : Entity
{
    private WalletTransaction()
    {
        Reference = string.Empty;
    }

    private WalletTransaction(Guid id, Guid walletId, WalletTransactionKind kind, decimal amount, decimal balanceAfter, string reference, string? note, Guid? byUserId, DateTimeOffset atUtc)
        : base(id)
    {
        WalletId = walletId;
        Kind = kind;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Reference = reference;
        Note = note;
        ByUserId = byUserId;
        AtUtc = atUtc;
    }

    public Guid WalletId { get; private set; }

    public WalletTransactionKind Kind { get; private set; }

    public decimal Amount { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public string Reference { get; private set; }

    public string? Note { get; private set; }

    public Guid? ByUserId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    internal static WalletTransaction Record(Guid walletId, WalletTransactionKind kind, decimal amount, decimal balanceAfter, string reference, string? note, Guid? byUserId, DateTimeOffset atUtc) =>
        new(Guid.CreateVersion7(), walletId, kind, amount, balanceAfter, reference, note, byUserId, atUtc);
}

/// <summary>A laboratory's request for credit (CP-66 "Request top-up"): Pending until the platform tops up (Completed) or declines.</summary>
public sealed class WalletTopUpRequest : Entity
{
    private WalletTopUpRequest()
    {
    }

    private WalletTopUpRequest(Guid id, Guid walletId, decimal amount, string? note, Guid requestedByUserId, DateTimeOffset requestedAtUtc)
        : base(id)
    {
        WalletId = walletId;
        Amount = amount;
        Note = note;
        RequestedByUserId = requestedByUserId;
        RequestedAtUtc = requestedAtUtc;
        Status = TopUpRequestStatus.Pending;
    }

    public Guid WalletId { get; private set; }

    public decimal Amount { get; private set; }

    public string? Note { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public TopUpRequestStatus Status { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public Guid? TransactionId { get; private set; }

    internal static WalletTopUpRequest Create(Guid walletId, decimal amount, string? note, Guid requestedByUserId, DateTimeOffset nowUtc) =>
        new(Guid.CreateVersion7(), walletId, amount, note, requestedByUserId, nowUtc);

    internal void Complete(Guid transactionId, DateTimeOffset nowUtc)
    {
        RequirePending("complete");
        Status = TopUpRequestStatus.Completed;
        TransactionId = transactionId;
        DecidedAtUtc = nowUtc;
    }

    internal void Decline(DateTimeOffset nowUtc)
    {
        RequirePending("decline");
        Status = TopUpRequestStatus.Declined;
        DecidedAtUtc = nowUtc;
    }

    private void RequirePending(string action)
    {
        if (Status != TopUpRequestStatus.Pending)
        {
            throw new InvalidStateTransitionException(PlatformAdminCodes.TopUpRequestNotPending, "top-up request", Status.ToString(), action);
        }
    }
}
