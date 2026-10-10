using Microsoft.EntityFrameworkCore.Metadata;
using Platform.ArchitectureTests.Support;
using Platform.Domain.Billing;

namespace Platform.ArchitectureTests;

/// <summary>
/// Rules the Billing module introduces (intake LIS-INT-BIL-001): every money column is <c>numeric(14,2)</c> (two decimals,
/// INV-BIL-009), payments and refunds live inside the visit account (one consistency boundary for BR-14/BR-26), and the
/// money tables are immutable in the database (INV-BIL-004, pitfall P-54).
/// </summary>
public sealed class BillingGateTests
{
    private static readonly IModel Model = OfflineModel.Build();

    private static readonly Type[] MoneyTypes = [typeof(VisitAccount), typeof(Payment), typeof(Refund), typeof(CashClosing), typeof(ClosingTotals)];

    [Fact]
    public void Every_money_column_of_billing_is_numeric_14_2()
    {
        var offenders = Model.GetEntityTypes()
            .Where(type => MoneyTypes.Contains(type.ClrType))
            .SelectMany(type => type.GetProperties().Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(decimal)).Select(property => (type, property)))
            .Where(pair => pair.property.GetPrecision() != BillingAmounts.Precision || pair.property.GetScale() != BillingAmounts.Scale)
            .Select(pair => $"{pair.type.ClrType.Name}.{pair.property.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Money columns without numeric(14,2): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Payments_and_refunds_are_reached_only_through_the_visit_account()
    {
        foreach (var child in new[] { typeof(Payment), typeof(Refund) })
        {
            var entity = Model.FindEntityType(child)!;
            var ownership = entity.GetForeignKeys().Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(VisitAccount));
            Assert.Equal(["TenantId", "VisitId"], ownership.Properties.Select(property => property.Name).ToArray());
            Assert.False(child.IsSubclassOf(typeof(SharedKernel.Primitives.AggregateRoot)), $"{child.Name} must stay an entity of VisitAccount");
            Assert.Empty(child.GetConstructors());   // created only by the account's transitions
        }
    }

    [Fact]
    public void The_billing_migration_sql_makes_the_money_tables_immutable()
    {
        var path = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Infrastructure", "Persistence", "Billing", "Billing.migration.sql");
        var sql = File.ReadAllText(path);

        Assert.Contains("CREATE TRIGGER payments_guard BEFORE UPDATE OR DELETE ON app.payments", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER refunds_immutable BEFORE UPDATE OR DELETE ON app.refunds", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER cash_closings_immutable BEFORE UPDATE OR DELETE ON app.cash_closings", sql, StringComparison.Ordinal);
        Assert.Contains("IF OLD.status = 'Voided' THEN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("bypass_rls", sql, StringComparison.Ordinal);
    }
}
