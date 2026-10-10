using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Account;
using Platform.Application.PlatformAdmin;
using Platform.ArchitectureTests.Support;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence.Account;
using Platform.Infrastructure.Persistence.Reporting;

namespace Platform.ArchitectureTests;

/// <summary>
/// Rules the Reporting module introduces in Release 1 (intake LIS-INT-REP-001 §8–§9): the cross-tenant activity of SA-03
/// is read through a platform-only <c>SECURITY DEFINER</c> function that returns counts, never by elevating in C#; the
/// real adapter replaces the platform module's zero fallback by convention; the CP-05 result figures are computed from
/// the agreed <c>app.results</c> columns and the BR-13 threshold, never from another module's entity types.
/// </summary>
public sealed class ReportingGateTests
{
    private static readonly string SqlPath = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Infrastructure", "Persistence", "Reporting", "Reporting.migration.sql");

    [Fact]
    public void The_activity_function_is_a_platform_only_security_definer_that_returns_counts()
    {
        var sql = File.ReadAllText(SqlPath);

        Assert.Contains("CREATE OR REPLACE FUNCTION saas.client_activity_counts(p_from timestamptz, p_to timestamptz)", sql, StringComparison.Ordinal);
        Assert.Contains("RETURNS TABLE (tenant_id uuid, served_patients bigint, tests bigint, registrations bigint)", sql, StringComparison.Ordinal);
        Assert.Contains("SECURITY DEFINER", sql, StringComparison.Ordinal);
        Assert.Contains("SET search_path = pg_catalog, pg_temp", sql, StringComparison.Ordinal);
        Assert.Contains("current_setting('app.current_tenant', true), '') <> '00000000-0000-0000-0000-000000000001'", sql, StringComparison.Ordinal);
        Assert.Contains("ERRCODE = '42501'", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) FROM PUBLIC;", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) TO lis_app;", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("bypass_rls", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("patient_id,", sql.Split("RETURNS TABLE")[1].Split("LANGUAGE")[0], StringComparison.Ordinal);   // counts only, never ids of patients
    }

    [Fact]
    public void The_activity_reader_replaces_the_zero_fallback_and_never_elevates()
    {
        Assert.False(typeof(ClientActivityReader).IsDefined(typeof(FallbackAdapterAttribute), inherit: false));
        Assert.True(typeof(IClientActivityReader).IsAssignableFrom(typeof(ClientActivityReader)));

        var parameters = typeof(ClientActivityReader).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToList();
        Assert.DoesNotContain(parameters, type => type.Name is "ICurrentTenantSetter" or "ITenantContextAccessor");

        var services = new ServiceCollection();
        services.AddInfrastructure("Host=127.0.0.1;Port=5433;Database=lis_gate;Username=lis_app");
        var registered = services.Single(descriptor => descriptor.ServiceType == typeof(IClientActivityReader));
        Assert.Equal(typeof(ClientActivityReader), registered.ImplementationType);
        Assert.Equal(typeof(BranchDashboardReader), services.Single(descriptor => descriptor.ServiceType == typeof(IBranchDashboardReader)).ImplementationType);
    }

    [Fact]
    public void The_result_figures_read_only_the_agreed_results_columns_and_the_turnaround_threshold()
    {
        string[] agreed = ["r.branch_id", "r.status", "r.visit_id", "r.tenant_id", "r.tube_id", "r.expected_at_utc"];
        foreach (var sql in new[] { BranchDashboardReader.PendingResultsSql, BranchDashboardReader.UnverifiedBacklogSql, BranchDashboardReader.DelayedTestsSql })
        {
            var used = System.Text.RegularExpressions.Regex.Matches(sql, @"\br\.[a-z_]+").Select(match => match.Value).Distinct().ToList();
            Assert.All(used, column => Assert.Contains(column, agreed));
            Assert.Contains("FROM app.results r", sql, StringComparison.Ordinal);
            Assert.Contains("r.branch_id = @branch", sql, StringComparison.Ordinal);
        }

        Assert.Contains("@late_factor", BranchDashboardReader.DelayedTestsSql, StringComparison.Ordinal);
        Assert.Contains("@as_of", BranchDashboardReader.DelayedTestsSql, StringComparison.Ordinal);
    }
}
