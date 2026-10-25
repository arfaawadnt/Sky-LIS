using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Platform.ArchitectureTests.Support;
using Platform.Domain.Billing;

namespace Platform.ArchitectureTests;

/// <summary>
/// Rules the cash ledger adds to Billing (change request LIS-CR-011, C-15): a cash entry's money is <c>numeric(14,2)</c> like every
/// money column of the module, the entry is immutable in the database except for its void, and the closing snapshot carries the
/// opening balance and the cash entries of its period.
/// </summary>
public sealed class CashLedgerGateTests
{
    private static readonly IModel Model = OfflineModel.Build();

    [Fact]
    public void A_cash_entry_keeps_its_money_in_numeric_14_2_and_its_key_per_tenant()
    {
        var entity = Model.FindEntityType(typeof(CashEntry))!;
        var amount = entity.FindProperty(nameof(CashEntry.Amount))!;
        Assert.Equal((BillingAmounts.Precision, BillingAmounts.Scale), (amount.GetPrecision(), amount.GetScale()));
        Assert.Equal("cash_entries", entity.GetTableName());
        Assert.Equal(["TenantId", "Id"], entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "Number"]));
    }

    [Fact]
    public void The_closing_snapshot_maps_the_opening_balance_and_the_cash_entries()
    {
        var totals = Model.FindEntityType(typeof(CashClosing))!.FindNavigation(nameof(CashClosing.Totals))!.TargetEntityType;
        foreach (var (property, column) in new[] { (nameof(ClosingTotals.OpeningBalance), "opening_balance"), (nameof(ClosingTotals.CashIn), "cash_in"), (nameof(ClosingTotals.CashOut), "cash_out") })
        {
            var mapped = totals.FindProperty(property)!;
            Assert.Equal((column, BillingAmounts.Precision, BillingAmounts.Scale, false), (mapped.GetColumnName(), mapped.GetPrecision(), mapped.GetScale(), mapped.IsNullable));
        }

        Assert.Equal("entry_count", totals.FindProperty(nameof(ClosingTotals.EntryCount))!.GetColumnName());
    }

    [Fact]
    public void The_billing_migration_sql_keeps_cash_entries_immutable_except_for_the_void()
    {
        var path = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Infrastructure", "Persistence", "Billing", "Billing.migration.sql");
        var sql = File.ReadAllText(path);

        Assert.Contains("CREATE TRIGGER cash_entries_guard BEFORE UPDATE OR DELETE ON app.cash_entries", sql, StringComparison.Ordinal);
        Assert.Contains("a voided cash entry is frozen", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("bypass_rls", sql, StringComparison.Ordinal);
    }
}
