using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>Intake §7 W1–W5 and INV-PLT-030/031: the prepaid wallet of a client.</summary>
public sealed class WalletTests
{
    private static readonly Guid Operator = Guid.CreateVersion7();
    private static readonly Guid LabUser = Guid.CreateVersion7();

    private static Wallet NewWallet() => Wallet.Open(Guid.CreateVersion7(), "egp", Now.AddDays(14));

    [Fact]
    public void A_new_wallet_is_empty_in_the_country_currency()
    {
        var wallet = NewWallet();
        Assert.Equal((0m, "EGP", Now.AddDays(14)), (wallet.Balance, wallet.CurrencyCode, wallet.NextChargeAtUtc));
        Assert.True(wallet.IsAtOrBelowZero);
    }

    [Fact]
    public void W1_top_up_credits_and_records_the_balance_after()
    {
        var wallet = NewWallet();
        var transaction = wallet.TopUp(500.01m, " INV-1 ", "bank transfer", Operator, Now);

        Assert.Equal(500.01m, wallet.Balance);
        Assert.Equal((WalletTransactionKind.TopUp, 500.01m, 500.01m, "INV-1", Operator), (transaction.Kind, transaction.Amount, transaction.BalanceAfter, transaction.Reference, transaction.ByUserId));
        var raised = Assert.IsType<WalletToppedUp>(Assert.Single(wallet.DomainEvents));
        Assert.Equal((transaction.Id, 500.01m), (raised.TransactionId, raised.Amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void W1_a_non_positive_amount_is_refused_WAL_001(decimal amount)
    {
        Assert.Equal(PlatformAdminCodes.AmountInvalid, Assert.Throws<DomainException>(() => NewWallet().TopUp(amount, "INV", null, Operator, Now)).Code);
    }

    [Theory]
    [InlineData("250.555")]
    [InlineData("0.001")]
    [InlineData("10.0001")]
    public void C_19_an_amount_with_more_than_two_decimals_is_refused_never_rounded_WAL_001(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var wallet = NewWallet();
        Assert.Equal(PlatformAdminCodes.AmountInvalid, Assert.Throws<DomainException>(() => wallet.TopUp(amount, "INV", null, Operator, Now)).Code);
        Assert.Equal(PlatformAdminCodes.AmountInvalid, Assert.Throws<DomainException>(() => wallet.RequestTopUp(amount, null, LabUser, Now)).Code);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(250.50m, wallet.TopUp(250.500m, "INV", null, Operator, Now).Amount);   // trailing zeros are two decimals
    }

    [Fact]
    public void W1_a_reference_is_required_WAL_002()
    {
        Assert.Equal(PlatformAdminCodes.ReferenceRequired, Assert.Throws<DomainException>(() => NewWallet().TopUp(10m, " ", null, Operator, Now)).Code);
    }

    [Fact]
    public void W2_the_subscription_charge_may_go_below_zero_and_moves_the_next_charge()
    {
        var wallet = NewWallet();
        wallet.TopUp(1000m, "INV-1", null, Operator, Now);
        var charge = wallet.ChargeSubscription(1500m, "SUB-2026-10", Now.AddDays(14));

        Assert.Equal(-500m, wallet.Balance);
        Assert.Equal((-1500m, -500m), (charge.Amount, charge.BalanceAfter));
        Assert.Equal(Now.AddDays(14).AddMonths(1), wallet.NextChargeAtUtc);
        Assert.True(wallet.IsAtOrBelowZero);
    }

    [Fact]
    public void W3_usage_is_debited_while_covered_and_refused_beyond_WAL_010()
    {
        var wallet = NewWallet();
        wallet.TopUp(1m, "INV-1", null, Operator, Now);
        wallet.DebitUsage(0.75m, "sms", Now);

        Assert.Equal(0.25m, wallet.Balance);
        var refusal = Assert.Throws<DomainException>(() => wallet.DebitUsage(0.5m, "sms", Now));
        Assert.Equal(PlatformAdminCodes.InsufficientBalance, refusal.Code);
        Assert.Equal(0.25m, wallet.Balance);
    }

    [Fact]
    public void W4_a_laboratory_requests_a_top_up_and_the_platform_settles_it()
    {
        var wallet = NewWallet();
        var request = wallet.RequestTopUp(2000m, "for October", LabUser, Now);
        Assert.Equal(TopUpRequestStatus.Pending, request.Status);
        Assert.Contains(wallet.DomainEvents, raised => raised is WalletTopUpRequested);

        var transaction = wallet.TopUp(2000m, "INV-9", null, Operator, Now.AddHours(1), request);

        Assert.Equal((TopUpRequestStatus.Completed, transaction.Id, Now.AddHours(1)), (request.Status, request.TransactionId, request.DecidedAtUtc));
        var twice = Assert.Throws<InvalidStateTransitionException>(() => wallet.DeclineTopUp(request, Now));
        Assert.Equal(PlatformAdminCodes.TopUpRequestNotPending, twice.Code);
    }

    [Fact]
    public void W5_a_pending_request_is_declined()
    {
        var wallet = NewWallet();
        var request = wallet.RequestTopUp(10m, null, LabUser, Now);
        wallet.DeclineTopUp(request, Now);
        Assert.Equal(TopUpRequestStatus.Declined, request.Status);
    }

    [Fact]
    public void A_request_of_another_wallet_is_refused_WAL_405()
    {
        var other = NewWallet();
        var foreign = other.RequestTopUp(10m, null, LabUser, Now);
        var wallet = NewWallet();

        Assert.Equal(PlatformAdminCodes.TopUpRequestNotFound, Assert.Throws<DomainException>(() => wallet.Attach(foreign)).Code);
        Assert.Equal(PlatformAdminCodes.TopUpRequestNotFound, Assert.Throws<DomainException>(() => wallet.TopUp(10m, "INV", null, Operator, Now, foreign)).Code);
    }
}
