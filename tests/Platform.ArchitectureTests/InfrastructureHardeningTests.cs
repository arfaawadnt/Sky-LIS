using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using Platform.ArchitectureTests.Support;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Interceptors;

namespace Platform.ArchitectureTests;

/// <summary>
/// Infrastructure rules of 01 §8: repositories never save, Npgsql stays inside Infrastructure, the connection
/// string keeps pool resets, and the interceptors register in their fixed order (connection stamp first).
/// </summary>
public sealed class InfrastructureHardeningTests
{
    private static readonly string[] NpgsqlNamespace = ["Npgsql"];

    [Fact]
    public void Repositories_never_call_SaveChanges()
    {
        var persistence = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Infrastructure", "Persistence");
        var offenders = Directory.EnumerateFiles(persistence, "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains("Repositor", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("SaveChanges", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "Repository sources that save (the handler commits once through IUnitOfWork): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Npgsql_is_used_only_inside_Infrastructure()
    {
        foreach (var assembly in new[] { SolutionAssemblies.ApplicationAssembly, SolutionAssemblies.DomainAssembly, SolutionAssemblies.ContractsAssembly, SolutionAssemblies.WebApiAssembly })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(NpgsqlNamespace).GetResult();
            Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} uses Npgsql through: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void A_connection_string_that_disables_pool_resets_is_refused()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddInfrastructure("Host=x;Database=y;Username=z;No Reset On Close=true"));
    }

    [Fact]
    public void Interceptors_register_in_the_fixed_order_with_the_connection_stamp_first()
    {
        // InterceptorOrderTests (01 §8): connection stamp first; on save tenant stamp → audit stamp → field changes → audit trail → outbox.
        // The trail reads the events while they are still on the aggregates; the outbox interceptor drains them, so it runs last.
        using var provider = new ServiceCollection().AddInfrastructure(OfflineModel.ConnectionString).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors?.Select(interceptor => interceptor.GetType()).ToList() ?? [];

        Assert.Equal(
            [typeof(TenantConnectionInterceptor), typeof(TenantStampInterceptor), typeof(AuditStampInterceptor), typeof(FieldChangeInterceptor), typeof(AuditTrailInterceptor), typeof(OutboxInterceptor)],
            interceptors);
        Assert.IsAssignableFrom<IDbConnectionInterceptor>(scope.ServiceProvider.GetRequiredService<TenantConnectionInterceptor>());
    }

    [Fact]
    public void The_database_guard_refuses_a_target_that_is_not_LIS()
    {
        // Isolation (CLAUDE.md §1): this host runs other applications; their databases and roles are never reachable from LIS.
        Assert.NotEmpty(Platform.Infrastructure.Configuration.DatabaseRoleGuard.InspectConnectionTarget("Host=127.0.0.1;Port=5432;Database=ntqams;Username=qams_app"));
        Assert.NotEmpty(Platform.Infrastructure.Configuration.DatabaseRoleGuard.InspectConnectionTarget("Host=127.0.0.1;Port=5433;Database=lis;Username=postgres"));
        Assert.NotEmpty(Platform.Infrastructure.Configuration.DatabaseRoleGuard.InspectConnectionTarget("Host=127.0.0.1;Port=5433;Username=lis_app"));
        Assert.Empty(Platform.Infrastructure.Configuration.DatabaseRoleGuard.InspectConnectionTarget("Host=127.0.0.1;Port=5433;Database=lis;Username=lis_app;Password=x"));
        Assert.Empty(Platform.Infrastructure.Configuration.DatabaseRoleGuard.InspectConnectionTarget("Host=db;Port=5432;Database=lis_test;Username=lis_owner;Password=x"));
        Assert.DoesNotContain("Password", Platform.Infrastructure.Configuration.DatabaseRoleGuard.Describe("Host=db;Database=lis;Username=lis_app;Password=secret"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_tenant_stamp_is_session_scoped_not_transaction_local()
    {
        // 01 §4.5 / P-05: SET LOCAL outside a transaction is a no-op; set_config(..., false) survives until DISCARD ALL.
        Assert.Contains("set_config('app.current_tenant', @tenant, false)", TenantConnectionInterceptor.StampSql, StringComparison.Ordinal);
        Assert.Contains("set_config('app.bypass_rls', @bypass, false)", TenantConnectionInterceptor.StampSql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET LOCAL", TenantConnectionInterceptor.StampSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_the_unit_of_work_and_the_allow_listed_stores_save()
    {
        var persistence = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Infrastructure");
        var savers = Directory.EnumerateFiles(persistence, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("Migrations", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("SaveChangesAsync(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // The envelope key store commits its own key-generation transaction under an advisory lock (02 §3.7); the one-time
        // code service persists a wrong attempt before the caller refuses (like the lockout counter); neither is on a command path.
        Assert.Equal(["EnvelopeKeyStore.cs", "OtpService.cs", "UnitOfWork.cs"], savers);
    }

    [Fact]
    public void The_audit_tables_live_in_the_audit_schema_without_a_row_version_and_the_tenant_ones_are_fenced()
    {
        // 01 §4.8: append-only ledgers carry no concurrency token (nothing updates them) and the two tenant ledgers are tenant-keyed.
        var model = OfflineModel.Build();
        var audit = model.GetEntityTypes().Where(type => type.GetSchema() == AppDbContext.AuditSchema).ToList();

        Assert.Equal(["audit_trail", "field_changes", "security_events"], audit.Select(type => type.GetTableName()).Order(StringComparer.Ordinal).ToList());
        Assert.All(audit, type => Assert.Null(type.FindProperty("RowVersion")));
        Assert.All(audit.Where(type => typeof(Platform.SharedKernel.MultiTenancy.ITenantScoped).IsAssignableFrom(type.ClrType)), type =>
        {
            Assert.Equal("tenant_id", type.FindPrimaryKey()!.Properties[0].GetColumnName());
            Assert.NotNull(type.GetQueryFilter());
        });
        Assert.False(FieldChangeInterceptor.IsTracked(model.FindEntityType(typeof(Platform.Infrastructure.Persistence.Audit.FieldChange))!), "the field-change interceptor must never audit its own rows");
        Assert.False(FieldChangeInterceptor.IsTracked(model.FindEntityType(typeof(Platform.Domain.Identity.RefreshSession))!), "sessions are not field-audited");
        Assert.False(FieldChangeInterceptor.IsTracked(model.FindEntityType(typeof(Platform.Infrastructure.Persistence.Outbox.OutboxEvent))!), "outbox rows are not field-audited");
        Assert.True(FieldChangeInterceptor.IsTracked(model.FindEntityType(typeof(Platform.Domain.Billing.CashClosing))!), "aggregates are field-audited");
        Assert.True(FieldChangeInterceptor.IsSkippedProperty("PasswordHash"));
        Assert.True(FieldChangeInterceptor.IsSkippedProperty("RowVersion"));
        Assert.False(FieldChangeInterceptor.IsSkippedProperty("Status"));
    }

    [Fact]
    public void The_migration_installs_append_only_triggers_and_a_null_tolerant_insert_policy_on_security_events_only()
    {
        var migrations = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Infrastructure", "Persistence", "Migrations");
        var sources = Directory.EnumerateFiles(migrations, "*.cs").Where(path => !path.EndsWith("Designer.cs", StringComparison.Ordinal) && !path.EndsWith("Snapshot.cs", StringComparison.Ordinal)).Select(File.ReadAllText).ToList();
        var all = string.Join('\n', sources);

        foreach (var table in new[] { "field_changes", "audit_trail", "security_events" })
        {
            Assert.Contains($"BEFORE UPDATE OR DELETE ON audit.{table}", all, StringComparison.Ordinal);
        }

        Assert.Contains("CREATE POLICY tenant_isolation ON audit.security_events", all, StringComparison.Ordinal);
    }
}
