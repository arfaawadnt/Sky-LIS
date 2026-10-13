using Platform.Domain.Billing;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Billing;

/// <summary>The collections report arithmetic and its laboratory-day period (change request 2026-10-13, item 2; intake §16 S-CR).</summary>
public sealed class CollectionsReportTests
{
    private static readonly Guid BranchA = Guid.CreateVersion7();
    private static readonly Guid BranchB = Guid.CreateVersion7();
    private static readonly Guid Amal = Guid.CreateVersion7();
    private static readonly Guid Basem = Guid.CreateVersion7();

    // Amal: cash 300 in, cash 50 back, card 100; Basem: cash 400 in branch A and POS 30 in branch B.
    private static readonly CollectionsCell[] Day =
    [
        new(BranchA, Amal, PaymentMethod.Cash, 300m, 2, 50m, 1),
        new(BranchA, Amal, PaymentMethod.Card, 100m, 1, 0m, 0),
        new(BranchA, Basem, PaymentMethod.Cash, 400m, 1, 0m, 0),
        new(BranchB, Basem, PaymentMethod.Pos, 30m, 1, 0m, 0),
    ];

    private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("Test+03", TimeSpan.FromHours(3), "Test +03", "Test +03");

    [Fact]
    public void S_CR_method_rows_sum_every_cashier_and_the_totals_are_the_net_per_method()
    {
        var (groups, totals) = CollectionsReport.Group(Day, CollectionsReportGrouping.Method);

        var cash = Assert.Single(groups, group => group.Key == "Cash").Figures;
        Assert.Equal((700m, 50m, 650m, 3, 1), (cash.Cash.Payments, cash.Cash.Refunds, cash.Cash.Net, cash.PaymentCount, cash.RefundCount));
        Assert.Equal(["Card", "Cash", "Pos"], groups.Select(group => group.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal((830m, 50m, 780m, 5, 1), (totals.TotalPayments, totals.TotalRefunds, totals.Net, totals.PaymentCount, totals.RefundCount));
        Assert.Equal((650m, 100m, 30m, 0m), (totals.Cash.Net, totals.Card.Net, totals.Pos.Net, totals.Transfer.Net));
    }

    [Fact]
    public void S_CR_user_and_branch_rows_carry_every_method_of_the_key()
    {
        var (byUser, _) = CollectionsReport.Group(Day, CollectionsReportGrouping.User);
        var amal = Assert.Single(byUser, group => group.Key == Amal.ToString("D")).Figures;
        Assert.Equal((300m, 50m, 100m, 400m, 50m, 350m), (amal.Cash.Payments, amal.Cash.Refunds, amal.Card.Payments, amal.TotalPayments, amal.TotalRefunds, amal.Net));
        var basem = Assert.Single(byUser, group => group.Key == Basem.ToString("D")).Figures;
        Assert.Equal((400m, 30m, 430m, 2), (basem.Cash.Payments, basem.Pos.Payments, basem.Net, basem.PaymentCount));

        var (byBranch, totals) = CollectionsReport.Group(Day, CollectionsReportGrouping.Branch);
        Assert.Equal(2, byBranch.Count);
        Assert.Equal(750m, Assert.Single(byBranch, group => group.Key == BranchA.ToString("D")).Figures.Net);
        Assert.Equal(30m, Assert.Single(byBranch, group => group.Key == BranchB.ToString("D")).Figures.Net);
        Assert.Equal(780m, totals.Net);
    }

    [Fact]
    public void S_CR_an_empty_day_has_no_rows_and_zero_totals()
    {
        var (groups, totals) = CollectionsReport.Group([], CollectionsReportGrouping.Method);

        Assert.Empty(groups);
        Assert.Equal(CollectionsFigures.Empty, totals);
        Assert.Equal(0m, totals.Net);
    }

    [Fact]
    public void S_CR_the_laboratory_day_and_the_period_follow_the_zone_and_the_to_day_is_inclusive()
    {
        var lateEvening = new DateTimeOffset(2026, 10, 9, 22, 30, 0, TimeSpan.Zero);   // 01:30 on 10 Oct at +03

        Assert.Equal(new DateOnly(2026, 10, 10), CollectionsReport.LocalDay(lateEvening, PlusThree));
        Assert.Equal(new DateOnly(2026, 10, 9), CollectionsReport.LocalDay(lateEvening, null));

        var (fromUtc, toUtc) = CollectionsReport.Period(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10), PlusThree);
        Assert.Equal((new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 10, 21, 0, 0, TimeSpan.Zero)), (fromUtc, toUtc));
        Assert.Equal(TimeSpan.Zero, fromUtc.Offset);

        var (utcFrom, utcTo) = CollectionsReport.Period(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3), null);
        Assert.Equal((new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero)), (utcFrom, utcTo));
    }

    [Fact]
    public void S_CR_a_period_ending_before_it_starts_is_BIL_030()
    {
        var refusal = Assert.Throws<DomainException>(() => CollectionsReport.Period(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 9), PlusThree));
        Assert.Equal((BillingCodes.PeriodInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void S_CR_the_report_days_limit_refuses_an_earlier_start_with_BIL_045_and_no_limit_allows_any()
    {
        var earliest = new DateOnly(2026, 10, 9);

        CollectionsReport.RequireWithinLimit(new DateOnly(2026, 10, 9), earliest);
        CollectionsReport.RequireWithinLimit(new DateOnly(2026, 1, 1), null);
        var refusal = Assert.Throws<DomainException>(() => CollectionsReport.RequireWithinLimit(new DateOnly(2026, 10, 8), earliest));
        Assert.Equal((BillingCodes.ReportPeriodBeyondLimit, DomainFault.Refused), (refusal.Code, refusal.Fault));
    }
}
