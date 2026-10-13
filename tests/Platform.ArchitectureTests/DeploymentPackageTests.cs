using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.ArchitectureTests.Support;
using Platform.WebApi.Startup;

namespace Platform.ArchitectureTests;

/// <summary>
/// Gates on the deployment package (deploy/windows, docs/deploy; runbook docs/deploy/Release-1_Deployment_Runbook.md):
/// the scripts stay inside the LIS reserved names of CLAUDE.md §2 and never name another application's resources,
/// configuration reaches the host only under the <c>Lis</c> root, the production template carries every key the
/// host reads (derived from the source, as WebApiHardeningTests does) with placeholders instead of secrets, and the
/// web.config proxies exactly the three API prefixes on the same origin.
/// </summary>
public sealed partial class DeploymentPackageTests
{
    private static readonly string Root = SolutionAssemblies.RepositoryRoot;
    private static readonly string DeployFolder = Path.Combine(Root, "deploy");
    private static readonly string WindowsFolder = Path.Combine(DeployFolder, "windows");
    private static readonly string RunbookPath = Path.Combine(Root, "docs", "deploy", "Release-1_Deployment_Runbook.md");
    private static readonly string TemplatePath = Path.Combine(WindowsFolder, "appsettings.Production.template.json");
    private static readonly string WebConfigPath = Path.Combine(WindowsFolder, "web.config");

    private static readonly string[] StateChangingScripts = ["publish.ps1", "install-api-service.ps1", "install-ui-site.ps1", "backup-database.ps1", "restore-database.ps1"];
    private static readonly string[] AllScripts = [.. StateChangingScripts, "smoke-test.ps1"];

    /// <summary>Drive-letter paths the package may name (CLAUDE.md §2 plus the tool folders of PostgreSQL and IIS).</summary>
    private static readonly string[] AllowedPathRoots =
    [
        @"E:\LIS", @"E:\lis-data", @"E:\lis-backups",
        @"C:\lis-app", @"C:\inetpub\wwwroot\lis-ui", @"C:\inetpub\wwwroot",   // the site root itself is only ever joined with lis-ui*
        @"C:\Program Files\PostgreSQL", @"C:\Program Files\IIS", @"C:\Program",
    ];

    private static readonly string[] AllowedTemplateRoots = ["Logging", "AllowedHosts", "Kestrel", "Lis"];

    private static readonly string[] SecretKeys = ["Lis:Database:ConnectionString", "Lis:Jwt:SigningKey", "Lis:Jwt:SubjectEncryptionKey", "Lis:DataProtection:MasterKey"];

    /// <summary>Every text file of the package: scripts, templates, SQL, compose and the runbook.</summary>
    private static IReadOnlyList<(string Name, string Text)> PackageFiles { get; } = Directory
        .EnumerateFiles(DeployFolder, "*.*", SearchOption.AllDirectories)
        .Where(path => !path.EndsWith(".env", StringComparison.OrdinalIgnoreCase))
        .Concat(File.Exists(RunbookPath) ? [RunbookPath] : Array.Empty<string>())
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => (Path.GetRelativePath(Root, path), File.ReadAllText(path)))
        .ToList();

    // ----------------------------------------------------------------------------------------------------------- isolation

    /// <summary>Fragments that name another application on this host or its ports (CLAUDE.md §1, §2). Pure, for the self-test.</summary>
    public static IReadOnlyList<string> IsolationViolations(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var violations = new List<string>();
        foreach (Match match in ForeignApplicationName().Matches(text))
        {
            violations.Add($"foreign application name '{match.Value}'");
        }

        foreach (Match match in ForeignPort().Matches(text))
        {
            violations.Add($"foreign port '{match.Value.Trim()}'");
        }

        foreach (Match match in ForeignDatabaseOrRole().Matches(text))
        {
            violations.Add($"foreign database or role '{match.Value}'");
        }

        return violations;
    }

    /// <summary>Configuration that bypasses the <c>Lis</c> root in a script or document: a generic environment assignment another application owns. Pure, for the self-test.</summary>
    public static IReadOnlyList<string> ConfigurationRootViolations(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var violations = new List<string>();
        foreach (Match match in EnvironmentAssignment().Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (!name.StartsWith("Lis__", StringComparison.Ordinal))
            {
                violations.Add($"environment assignment '{name}=' is outside the Lis root");
            }
        }

        foreach (Match match in UrlsAssignment().Matches(text))
        {
            violations.Add($"environment assignment '{match.Groups["name"].Value}=' overrides the pinned Kestrel endpoint");
        }

        return violations;
    }

    /// <summary>Top-level sections of a configuration file that are not the host's own (<c>Logging</c>, <c>AllowedHosts</c>, <c>Kestrel</c>) or <c>Lis</c>. Pure, for the self-test.</summary>
    public static IReadOnlyList<string> JsonRootViolations(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !AllowedTemplateRoots.Contains(name))
            .Select(name => $"top-level section '{name}' is outside the Lis root")
            .ToList();
    }

    [Fact]
    public void The_package_is_complete()
    {
        foreach (var script in AllScripts)
        {
            Assert.True(File.Exists(Path.Combine(WindowsFolder, script)), $"deploy/windows/{script} is missing");
        }

        Assert.True(File.Exists(WebConfigPath), "deploy/windows/web.config is missing");
        Assert.True(File.Exists(TemplatePath), "deploy/windows/appsettings.Production.template.json is missing");
        Assert.True(File.Exists(RunbookPath), "docs/deploy/Release-1_Deployment_Runbook.md is missing");
        Assert.True(PackageFiles.Count >= AllScripts.Length + 3, "the package scan found fewer files than the package holds");
    }

    [Fact]
    public void No_package_file_names_another_application_or_its_ports()
    {
        foreach (var (name, text) in PackageFiles)
        {
            var violations = IsolationViolations(text);
            Assert.True(violations.Count == 0, $"{name}: {string.Join("; ", violations)}");
        }
    }

    [Fact]
    public void No_package_file_configures_outside_the_Lis_root()
    {
        foreach (var (name, text) in PackageFiles)
        {
            var violations = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? JsonRootViolations(text) : ConfigurationRootViolations(text);
            Assert.True(violations.Count == 0, $"{name}: {string.Join("; ", violations)}");
        }
    }

    [Fact]
    public void Every_drive_path_in_the_package_is_a_reserved_LIS_location()
    {
        foreach (var (name, text) in PackageFiles)
        {
            foreach (Match match in DrivePath().Matches(text))
            {
                var path = match.Value;
                Assert.True(AllowedPathRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)), $"{name} names '{path}', which is not a reserved LIS location (CLAUDE.md §2)");
            }
        }
    }

    [Fact]
    public void The_isolation_gate_flags_foreign_names_and_accepts_LIS_names()
    {
        // Self-test (pitfall P-71): the scanner finds the names it is meant to find and ignores legitimate numbers.
        var flagged = IsolationViolations("Database=ntqams;Port=5080 and http://localhost:4200 and the QAMS site");
        Assert.Contains(flagged, v => v.Contains("ntqams", StringComparison.Ordinal));
        Assert.Contains(flagged, v => v.Contains("5080", StringComparison.Ordinal));
        Assert.Contains(flagged, v => v.Contains("4200", StringComparison.Ordinal));
        Assert.Contains(flagged, v => v.Contains("QAMS", StringComparison.Ordinal));
        Assert.Contains(IsolationViolations("server listening on port 5000"), v => v.Contains("5000", StringComparison.Ordinal));
        Assert.Contains(IsolationViolations("Username=postgres;Database=lis"), v => v.Contains("postgres", StringComparison.Ordinal));

        Assert.Empty(IsolationViolations("Database=lis;Username=lis_app;Port=5433 http://127.0.0.1:5100 http://127.0.0.1:5180 actions= restart/5000/restart/10000 50000 bytes"));
        Assert.Empty(ConfigurationRootViolations("Lis__Database__ConnectionString=... ASPNETCORE_ENVIRONMENT=Production"));
        var generic = ConfigurationRootViolations("ConnectionStrings__Postgres=x; ASPNETCORE_URLS=http://+:5000");
        Assert.Contains(generic, v => v.Contains("ConnectionStrings__Postgres", StringComparison.Ordinal));
        Assert.Contains(generic, v => v.Contains("ASPNETCORE_URLS", StringComparison.Ordinal));
        Assert.Empty(JsonRootViolations("{ \"Logging\": {}, \"Kestrel\": {}, \"Lis\": { \"Jwt\": {} } }"));
        Assert.Contains(JsonRootViolations("{ \"Jwt\": {}, \"ConnectionStrings\": {} }"), v => v.Contains("'Jwt'", StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------------------------------------------------- scripts

    [Fact]
    public void Every_script_is_strict_and_the_state_changing_ones_support_WhatIf()
    {
        foreach (var script in AllScripts)
        {
            var text = File.ReadAllText(Path.Combine(WindowsFolder, script));
            Assert.Contains("[CmdletBinding(", text, StringComparison.Ordinal);
            Assert.Contains("Set-StrictMode -Version Latest", text, StringComparison.Ordinal);
            Assert.Contains("$ErrorActionPreference = 'Stop'", text, StringComparison.Ordinal);
            Assert.DoesNotContain("&&", text, StringComparison.Ordinal);   // Windows PowerShell 5.1 has no pipeline chain operators
            Assert.True(text.StartsWith("<#", StringComparison.Ordinal), $"{script} does not start with comment-based help");
            if (StateChangingScripts.Contains(script))
            {
                Assert.Contains("SupportsShouldProcess = $true", text, StringComparison.Ordinal);
                Assert.Contains("ShouldProcess(", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void No_script_embeds_a_password_or_key()
    {
        foreach (var (name, text) in PackageFiles)
        {
            foreach (Match match in PasswordAssignment().Matches(text))
            {
                var value = match.Groups["value"].Value;
                Assert.True(value.StartsWith("<SET:", StringComparison.Ordinal) || value.StartsWith("$", StringComparison.Ordinal) || value.StartsWith(":'", StringComparison.Ordinal) || value.StartsWith("…", StringComparison.Ordinal) || value.StartsWith("${", StringComparison.Ordinal) || value.Length == 0 || value.StartsWith("<", StringComparison.Ordinal),
                    $"{name} assigns a literal password: '{match.Value}'");
            }

            Assert.DoesNotContain("-----BEGIN", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_service_and_site_scripts_use_the_reserved_names_and_ports()
    {
        var api = File.ReadAllText(Path.Combine(WindowsFolder, "install-api-service.ps1"));
        Assert.Contains("'NTLIS_TEST'", api, StringComparison.Ordinal);
        Assert.Contains("'NTLIS'", api, StringComparison.Ordinal);
        Assert.Contains("5100", api, StringComparison.Ordinal);
        Assert.Contains("5180", api, StringComparison.Ordinal);
        Assert.Contains("C:\\lis-app", api, StringComparison.Ordinal);
        Assert.Contains("NT SERVICE\\$serviceName", api, StringComparison.Ordinal);   // a dedicated virtual account, no password
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", api, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Extensions.Hosting.WindowsServices.dll", api, StringComparison.Ordinal);
        Assert.Contains("/health/ready", api, StringComparison.Ordinal);

        var site = File.ReadAllText(Path.Combine(WindowsFolder, "install-ui-site.ps1"));
        Assert.Contains("'lis-ui-test'", site, StringComparison.Ordinal);
        Assert.Contains("'lis-ui'", site, StringComparison.Ordinal);
        Assert.Contains("8081", site, StringComparison.Ordinal);
        Assert.Contains("C:\\inetpub\\wwwroot", site, StringComparison.Ordinal);
        Assert.Contains("'RewriteModule'", site, StringComparison.Ordinal);
        Assert.Contains("'ApplicationRequestRouting'", site, StringComparison.Ordinal);
        Assert.Contains("__API_PORT__", site, StringComparison.Ordinal);
        Assert.Contains("__SCRIPT_HASHES__", site, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------------------------------------------------- web.config

    [Fact]
    public void The_web_config_proxies_the_three_api_prefixes_on_the_same_origin_and_serves_the_spa()
    {
        var text = File.ReadAllText(WebConfigPath);
        Assert.Contains("http://127.0.0.1:__API_PORT__/{R:0}", text, StringComparison.Ordinal);
        Assert.Contains(@"^(api|health|\.well-known)(/.*)?$", text, StringComparison.Ordinal);
        Assert.Contains("url=\"/index.html\"", text, StringComparison.Ordinal);
        Assert.Contains("matchType=\"IsFile\" negate=\"true\"", text, StringComparison.Ordinal);
        Assert.Contains("HTTP_X_FORWARDED_PROTO", text, StringComparison.Ordinal);
        Assert.DoesNotContain("http://127.0.0.1:5100", text, StringComparison.Ordinal);   // the port is a token, filled per service
        Assert.DoesNotContain("http://localhost", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_web_config_headers_match_the_api_security_headers_and_the_caching_rules()
    {
        var text = File.ReadAllText(WebConfigPath);
        Assert.Contains("value=\"nosniff\"", text, StringComparison.Ordinal);
        Assert.Contains("value=\"DENY\"", text, StringComparison.Ordinal);
        Assert.Contains("value=\"no-referrer\"", text, StringComparison.Ordinal);
        Assert.Contains($"value=\"{Platform.WebApi.Security.SecurityHeadersMiddleware.StrictTransportSecurity}\"", text, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", text, StringComparison.Ordinal);
        Assert.Contains("__SCRIPT_HASHES__", text, StringComparison.Ordinal);
        Assert.Contains("RESPONSE_Cache_Control", text, StringComparison.Ordinal);
        Assert.Contains("value=\"no-store\"", text, StringComparison.Ordinal);
        Assert.Contains("max-age=31536000, immutable", text, StringComparison.Ordinal);
        Assert.Contains("removeServerHeader=\"true\"", text, StringComparison.Ordinal);
        Assert.Contains("<remove name=\"X-Powered-By\" />", text, StringComparison.Ordinal);
        // Proxied responses keep the API's own headers: every header rule is scoped by a not-proxied pre-condition.
        Assert.Contains("name=\"lis-not-proxied\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<add name=\"X-Frame-Options\"", text, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------------------------------------------------- template

    [Fact]
    public void The_production_template_has_only_the_Lis_root_and_the_host_sections()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(TemplatePath));
        foreach (var property in document.RootElement.EnumerateObject())
        {
            Assert.True(AllowedTemplateRoots.Contains(property.Name), $"top-level section '{property.Name}' is not one of {string.Join(", ", AllowedTemplateRoots)}");
        }

        Assert.True(document.RootElement.TryGetProperty("Lis", out _), "the template has no Lis section");
        var url = document.RootElement.GetProperty("Kestrel").GetProperty("Endpoints").GetProperty("Http").GetProperty("Url").GetString();
        Assert.Equal("http://127.0.0.1:5100", url);
        Assert.Empty(StartupGuards.InspectBindings([url!]));
    }

    [Fact]
    public void The_production_template_carries_placeholders_not_secrets_and_no_relaxation()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(TemplatePath));
        foreach (var key in SecretKeys)
        {
            var value = Resolve(document.RootElement, key);
            Assert.True(value is { ValueKind: JsonValueKind.String }, $"{key} is missing from the template");
            Assert.Contains("<SET:", value!.Value.GetString(), StringComparison.Ordinal);
        }

        var text = File.ReadAllText(TemplatePath);
        Assert.DoesNotContain(StartupGuards.ForbiddenKeyFragment, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-----BEGIN", text, StringComparison.Ordinal);
        Assert.False(Resolve(document.RootElement, "Lis:Security:AllowInsecureLocalDevelopment")!.Value.GetBoolean(), "the template relaxes the cookie and HSTS");
        var lifetime = TimeSpan.Parse(Resolve(document.RootElement, "Lis:Security:Session:AccessTokenLifetime")!.Value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(lifetime <= Platform.Application.Identity.SessionPolicy.MaxAccessTokenLifetime, "the template exceeds the 15-minute access token ceiling (D-01)");
        Assert.Equal(@"E:\lis-data\files", Resolve(document.RootElement, "Lis:Files:Root")!.Value.GetString());
        var pickup = Resolve(document.RootElement, "Lis:Notifications:PickupDirectory")!.Value.GetString();
        Assert.Equal(@"E:\lis-data\mail-pickup", pickup);
        Assert.Empty(Platform.Infrastructure.Security.ConfigGuard.InspectNotifications(new Platform.Infrastructure.Notifications.NotificationOptions { PickupDirectory = pickup }, production: true));
        foreach (var secret in new[] { "Lis:Notifications:Smtp:Password", "Lis:Notifications:Sms:ApiKey" })
        {
            Assert.Equal(string.Empty, Resolve(document.RootElement, secret)!.Value.GetString());   // optional secrets: empty, filled only on the server
        }

        Assert.True(Platform.Infrastructure.Notifications.HttpSmsTransport.IsValidTemplate(Resolve(document.RootElement, "Lis:Notifications:Sms:BodyTemplate")!.Value.GetString()), "the template's SMS body template is not valid JSON with {to} and {text}");
        Assert.NotEmpty(Resolve(document.RootElement, "Lis:Security:AllowedOrigins")!.Value.EnumerateArray());
    }

    [Fact]
    public void Every_configuration_key_the_host_reads_appears_in_the_production_template()
    {
        var keys = ConfigurationKeysReadByTheHost();

        // Evidence that the derivation looked at the right places (pitfall P-71).
        Assert.True(keys.Count >= 40, $"only {keys.Count} keys were derived from the source: {string.Join(", ", keys)}");
        foreach (var expected in new[] { "Lis:Database:ConnectionString", "Lis:Jwt:SigningKey", "Lis:DataProtection:MasterKey", "Lis:Security:Session:AccessTokenLifetime", "Lis:Security:RateLimits:GlobalPerMinute", "Lis:Security:Envelope:RsaKeySize", "Lis:Outbox:Enabled", "Lis:Files:Root", "Lis:Platform:PortalDomain", "Lis:Security:AllowInsecureLocalDevelopment", "Lis:Notifications:PickupDirectory", "Lis:Notifications:Smtp:Host", "Lis:Notifications:Smtp:Password", "Lis:Notifications:Sms:Url", "Lis:Notifications:Sms:ApiKey", "Lis:Notifications:Sms:BodyTemplate" })
        {
            Assert.Contains(expected, keys);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(TemplatePath));
        var missing = keys.Where(key => Resolve(document.RootElement, key) is null).ToList();
        Assert.True(missing.Count == 0, "keys read by the host but absent from deploy/windows/appsettings.Production.template.json: " + string.Join(", ", missing));
    }

    // ----------------------------------------------------------------------------------------------------------- runbook and README

    [Fact]
    public void The_runbook_covers_every_script_and_the_bootstrap_sequence()
    {
        var runbook = File.ReadAllText(RunbookPath);
        foreach (var script in AllScripts)
        {
            Assert.Contains(script, runbook, StringComparison.Ordinal);
        }

        foreach (var step in new[] { "roles.sql", "schema.sql", "migrations.sql", "seed-platform-admin", "deploy/SECRETS.md", "appsettings.Production.template.json", "NTLIS", "lis-ui", "E:\\lis-backups", "Rollback" })
        {
            Assert.Contains(step, runbook, StringComparison.Ordinal);
        }

        var readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        Assert.Contains("## Deployment", readme, StringComparison.Ordinal);
        Assert.Contains("docs/deploy/Release-1_Deployment_Runbook.md", readme, StringComparison.Ordinal);

        var claude = File.ReadAllText(Path.Combine(Root, "CLAUDE.md"));
        Assert.Contains(@"C:\lis-app", claude, StringComparison.Ordinal);   // the install root is a reserved name (CLAUDE.md §2)
    }

    // ----------------------------------------------------------------------------------------------------------- derivation

    /// <summary>
    /// The <c>Lis:*</c> keys the host reads, derived from the source like WebApiHardeningTests derives header names:
    /// string literals <c>"Lis:…"</c> anywhere in src, the <c>Root + ":…"</c> sections of SecurityConfiguration with
    /// their indexers, <c>GetValue</c> and <c>GetSection</c> calls, and the public properties of every options type a
    /// section is bound to with <c>Get&lt;T&gt;()</c>.
    /// </summary>
    private static IReadOnlyList<string> ConfigurationKeysReadByTheHost()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var sources = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        foreach (var source in sources)
        {
            foreach (Match match in LisLiteral().Matches(source))
            {
                keys.Add(match.Groups["key"].Value.TrimEnd(':'));
            }

            foreach (Match match in RootIndexer().Matches(source))
            {
                keys.Add("Lis:" + match.Groups["key"].Value);
            }

            foreach (Match match in RootConstant().Matches(source))
            {
                keys.Add("Lis:" + match.Groups["key"].Value);
            }

            var sections = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in RootSection().Matches(source))
            {
                sections[match.Groups["variable"].Value] = "Lis:" + match.Groups["section"].Value;
            }

            foreach (var (variable, section) in sections)
            {
                foreach (Match match in Regex.Matches(source, $@"\b{Regex.Escape(variable)}\[""(?<key>[A-Za-z0-9]+)""\]"))
                {
                    keys.Add($"{section}:{match.Groups["key"].Value}");
                }

                foreach (Match match in Regex.Matches(source, $@"\b{Regex.Escape(variable)}\.GetValue\(""(?<key>[A-Za-z0-9]+)"""))
                {
                    keys.Add($"{section}:{match.Groups["key"].Value}");
                }

                foreach (Match match in Regex.Matches(source, $@"\b{Regex.Escape(variable)}\.GetSection\(""(?<key>[A-Za-z0-9]+)""\)(?:\.Get<(?<type>[A-Za-z0-9]+)>\(\))?"))
                {
                    var key = $"{section}:{match.Groups["key"].Value}";
                    keys.Add(key);
                    if (match.Groups["type"].Success)
                    {
                        AddBoundProperties(keys, key, match.Groups["type"].Value);
                    }
                }
            }

            foreach (Match match in BoundSection().Matches(source))
            {
                AddBoundProperties(keys, "Lis:" + match.Groups["section"].Value, match.Groups["type"].Value);
            }
        }

        // RevealOtpInDevelopmentLog mirrors AllowInsecureLocalDevelopment and has no key of its own (SecurityConfiguration.ReadOptions).
        return keys.ToList();
    }

    /// <summary>The public settable properties of an options type a section is bound to with <c>Get&lt;T&gt;()</c>, as keys under that section.</summary>
    private static void AddBoundProperties(ISet<string> keys, string section, string typeName)
    {
        var type = new[] { SolutionAssemblies.WebApiAssembly, SolutionAssemblies.ApplicationAssembly, SolutionAssemblies.InfrastructureAssembly }
            .SelectMany(assembly => assembly.GetTypes())
            .FirstOrDefault(candidate => candidate.Name == typeName && candidate.IsClass && candidate != typeof(string));
        if (type is null)
        {
            return;   // string[] bindings (AllowedOrigins, PreviousSubjectEncryptionKeys) are keys in their own right
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.SetMethod is not null))
        {
            keys.Add($"{section}:{property.Name}");
        }
    }

    private static JsonElement? Resolve(JsonElement root, string key)
    {
        var current = root;
        foreach (var segment in key.Split(':'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    [GeneratedRegex(@"(?i)\bntqams\b|qams|\bqms\b")]
    private static partial Regex ForeignApplicationName();

    [GeneratedRegex(@"(?i)(?::|\bport[\s=:]*)(?:5000|5080|4200)(?!\d)")]
    private static partial Regex ForeignPort();

    [GeneratedRegex(@"(?i)(?:Database|Username|User Id)\s*=\s*(?:ntqams|postgres)\b")]
    private static partial Regex ForeignDatabaseOrRole();

    [GeneratedRegex(@"\b(?<name>[A-Za-z]+__[A-Za-z0-9_]+)=")]
    private static partial Regex EnvironmentAssignment();

    [GeneratedRegex(@"\b(?<name>ASPNETCORE_URLS|DOTNET_URLS)\s*=")]
    private static partial Regex UrlsAssignment();

    [GeneratedRegex(@"\b[A-Za-z]:\\[A-Za-z0-9_.-]+(?:\\[A-Za-z0-9_.-]+)*")]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"(?i)\b(?:password|pwd|owner_pw|app_pw)\s*=\s*(?<value>[^;\s""'`)]*)")]
    private static partial Regex PasswordAssignment();

    [GeneratedRegex(@"""(?<key>Lis:[A-Za-z0-9]+(?::[A-Za-z0-9]+)*)""")]
    private static partial Regex LisLiteral();

    [GeneratedRegex(@"configuration\[Root \+ "":(?<key>[A-Za-z0-9:]+)""\]")]
    private static partial Regex RootIndexer();

    [GeneratedRegex(@"const string \w+ = Root \+ "":(?<key>[A-Za-z0-9:]+)"";")]
    private static partial Regex RootConstant();

    [GeneratedRegex(@"var (?<variable>[a-z][A-Za-z]*) = configuration\.GetSection\(Root \+ "":(?<section>[A-Za-z:]+)""\);")]
    private static partial Regex RootSection();

    [GeneratedRegex(@"GetSection\(Root \+ "":(?<section>[A-Za-z:]+)""\)\.Get<(?<type>[A-Za-z]+)(?:\[\])?>\(\)")]
    private static partial Regex BoundSection();
}
