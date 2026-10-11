using System.Text.RegularExpressions;
using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// Migration rules of 01 §4.6 on the migration sources: RLS declared in the migration that creates each tenant
/// table (pitfall P-04), no bypass incantation (P-05), no explicit xmin column, and a committed idempotent
/// deployment script that is current.
/// </summary>
public sealed partial class MigrationScriptTests
{
    private static readonly string MigrationsFolder = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Infrastructure", "Persistence", "Migrations");
    private static readonly string[] TenantlessAppTables = ["credential_nonces"];
    private static readonly string DeployScript = Path.Combine(SolutionAssemblies.RepositoryRoot, "deploy", "postgres", "migrations.sql");

    private static IReadOnlyList<(string Name, string Source)> Migrations { get; } = Directory
        .EnumerateFiles(MigrationsFolder, "*.cs")
        .Where(path => !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => (Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)))
        .ToList();

    [Fact]
    public void At_least_one_migration_exists()
    {
        Assert.NotEmpty(Migrations);
    }

    [Fact]
    public void Every_tenant_table_enables_and_forces_row_level_security_in_its_creating_migration()
    {
        foreach (var (name, source) in Migrations)
        {
            foreach (Match match in AppTableCreation().Matches(source))
            {
                var table = match.Groups["table"].Value;
                if (!HasTenantColumn(source, match.Index))
                {
                    // Pre-authentication tables in app.* carry no tenant and cannot be fenced by RLS (01 §4.1); each one is named here on purpose.
                    Assert.Contains(table, TenantlessAppTables);
                    continue;
                }

                Assert.True(source.Contains($"ALTER TABLE app.{table} ENABLE ROW LEVEL SECURITY", StringComparison.Ordinal), $"{name}: app.{table} is created without ENABLE ROW LEVEL SECURITY");
                Assert.True(source.Contains($"ALTER TABLE app.{table} FORCE ROW LEVEL SECURITY", StringComparison.Ordinal), $"{name}: app.{table} is created without FORCE ROW LEVEL SECURITY");
                Assert.True(source.Contains($"CREATE POLICY tenant_isolation ON app.{table}", StringComparison.Ordinal), $"{name}: app.{table} is created without the tenant_isolation policy");
                Assert.True(source.Contains("WITH CHECK", StringComparison.Ordinal), $"{name}: the tenant_isolation policy on app.{table} needs a WITH CHECK clause");
            }
        }
    }

    [Fact]
    public void No_migration_sets_the_bypass_flag()
    {
        foreach (var (name, source) in Migrations)
        {
            Assert.False(BypassIncantation().IsMatch(source), $"{name} sets app.bypass_rls; the migration role has BYPASSRLS instead (01 §4.6)");
        }
    }

    [Fact]
    public void The_deploy_script_records_every_migration()
    {
        // The committed idempotent script is what deploys; a migration added without regenerating it would never reach a server.
        Assert.True(File.Exists(DeployScript), $"{DeployScript} is missing");
        var script = File.ReadAllText(DeployScript);
        foreach (var (name, _) in Migrations)
        {
            Assert.True(
                script.Contains($"VALUES ('{name}', ", StringComparison.Ordinal),
                $"deploy/postgres/migrations.sql does not record migration {name}: regenerate it with dotnet ef migrations script --idempotent");
        }
    }

    [Fact]
    public void The_generated_sql_never_creates_an_xmin_column()
    {
        // The migration source carries the provider's xmin marker (rowVersion: true, type xid); the SQL generator must strip it.
        Assert.True(File.Exists(DeployScript), $"{DeployScript} is missing");
        var script = File.ReadAllText(DeployScript);
        Assert.DoesNotContain("xmin xid", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"xmin\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_tenant_policy_tolerates_a_null_tenant()
    {
        // P-30: a null-tolerant policy makes platform rows visible to every tenant. Only audit.* WITH CHECK may allow NULL.
        foreach (var (name, source) in Migrations)
        {
            foreach (Match match in TenantPolicy().Matches(source))
            {
                if (!match.Groups["schema"].Value.Equals("audit", StringComparison.Ordinal))
                {
                    Assert.DoesNotContain("IS NULL", match.Value, StringComparison.OrdinalIgnoreCase);
                }
            }

            Assert.True(name.Length > 0);
        }
    }

    [Fact]
    public void The_idempotent_deployment_script_is_committed_and_current()
    {
        Assert.True(File.Exists(DeployScript), $"{DeployScript} is missing; regenerate it with 'dotnet ef migrations script --idempotent'");

        var script = File.ReadAllText(DeployScript);
        var latest = Migrations[^1].Name;
        Assert.Contains(latest, script, StringComparison.Ordinal);
        Assert.Contains("FORCE ROW LEVEL SECURITY", script, StringComparison.Ordinal);
        Assert.Contains("CREATE POLICY tenant_isolation ON app.tenant_memberships", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_table_that_refuses_row_deletion_also_refuses_truncate()
    {
        // Validation 2026-10-11 D-01: a BEFORE DELETE row trigger never fires on TRUNCATE, so a table whose rows are never
        // deleted (append-only ledgers, money records, patient files, visits, results) needs a statement-level BEFORE TRUNCATE
        // refusal as well, or the owner role could empty it.
        var refusingDelete = Migrations
            .SelectMany(migration => RowDeleteTrigger().Matches(migration.Source).Select(match => match.Groups["table"].Value))
            .ToHashSet(StringComparer.Ordinal);
        var refusingTruncate = Migrations
            .SelectMany(migration => StatementTruncateTrigger().Matches(migration.Source).Select(match => match.Groups["table"].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(refusingDelete);
        var unguarded = refusingDelete.Where(table => !refusingTruncate.Contains(table)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unguarded.Count == 0, "Tables that refuse DELETE but not TRUNCATE (add a BEFORE TRUNCATE … FOR EACH STATEMENT trigger in a migration): " + string.Join(", ", unguarded));
    }

    private static bool HasTenantColumn(string source, int createTableIndex)
    {
        var end = source.IndexOf("constraints:", createTableIndex, StringComparison.Ordinal);
        var block = end < 0 ? source[createTableIndex..] : source[createTableIndex..end];
        return block.Contains("tenant_id = table.Column", StringComparison.Ordinal);
    }

    [GeneratedRegex("CreateTable\\(\\s*name:\\s*\"(?<table>[a-z_]+)\",\\s*schema:\\s*\"app\"")]
    private static partial Regex AppTableCreation();

    [GeneratedRegex("set_config\\(\\s*'app\\.bypass_rls'|SET\\s+(LOCAL\\s+)?app\\.bypass_rls", RegexOptions.IgnoreCase)]
    private static partial Regex BypassIncantation();

    [GeneratedRegex(@"CREATE\s+TRIGGER\s+\w+\s+BEFORE\s+(?:UPDATE\s+OR\s+)?DELETE(?:\s+OR\s+UPDATE)?\s+ON\s+(?<table>[a-z]+\.[a-z_]+)\s+FOR\s+EACH\s+ROW", RegexOptions.IgnoreCase)]
    private static partial Regex RowDeleteTrigger();

    [GeneratedRegex(@"CREATE\s+TRIGGER\s+\w+\s+BEFORE\s+TRUNCATE\s+ON\s+(?<table>[a-z]+\.[a-z_]+)\s+FOR\s+EACH\s+STATEMENT", RegexOptions.IgnoreCase)]
    private static partial Regex StatementTruncateTrigger();

    [GeneratedRegex("CREATE POLICY tenant_isolation ON (?<schema>[a-z]+)\\.[a-z_]+(?<body>[\\s\\S]*?);", RegexOptions.IgnoreCase)]
    private static partial Regex TenantPolicy();
}
