using MediatR;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// INV-PLT-070 (intake LIS-INT-PLT-001 §9): platform administration is reachable only from the platform tenant. Every
/// request of the module is either anonymous (the public wizard), tenant-facing by name (the laboratory's own wallet),
/// or marked <see cref="IPlatformAdministrationRequest"/>; and the handler of every marked request starts with
/// <c>PlatformAdminScope.Require(</c>, so a client role holding <c>tenancy.*</c> keys is refused with PLT-403.
/// </summary>
public sealed class PlatformAdminGateTests
{
    private const string ModuleNamespace = "Platform.Application.PlatformAdmin";
    private const string Guard = "PlatformAdminScope.Require(";
    private static readonly string[] TenantFacingRequests = ["GetMyWalletQuery", "ListMyWalletTransactionsQuery", "RequestWalletTopUpCommand"];
    private static readonly string ModuleFolder = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.Application", "PlatformAdmin");

    [Fact]
    public void Every_module_request_is_anonymous_tenant_facing_or_marked_as_platform_administration()
    {
        var requests = RequestPolicyGate.RequestsIn(SolutionAssemblies.ApplicationAssembly)
            .Where(type => type.Namespace?.StartsWith(ModuleNamespace, StringComparison.Ordinal) == true)
            .ToList();
        Assert.NotEmpty(requests);

        var unmarked = requests
            .Where(type => !typeof(IPlatformAdministrationRequest).IsAssignableFrom(type))
            .Where(type => Attribute.GetCustomAttribute(type, typeof(AllowUnauthenticatedAttribute)) is null)
            .Where(type => !TenantFacingRequests.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => type.Name)
            .ToList();
        Assert.True(unmarked.Count == 0, "Requests of the platform module that neither are anonymous, tenant-facing, nor IPlatformAdministrationRequest: " + string.Join(", ", unmarked));
    }

    [Fact]
    public void Every_platform_administration_handler_checks_the_platform_tenant_first()
    {
        var sources = Directory.EnumerateFiles(ModuleFolder, "*.cs", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        var handlers = HandlersOfMarkedRequests().ToList();
        Assert.NotEmpty(handlers);

        var offenders = handlers
            .Where(handler => !HandlerBodies(sources.Values, handler.Name).Any(body => FirstStatementIsGuard(body)))
            .Select(handler => handler.Name)
            .ToList();
        Assert.True(offenders.Count == 0, $"Handlers of platform administration requests must start with {Guard}…: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_body_extraction_finds_a_guarded_handler_and_flags_an_unguarded_one()
    {
        const string Guarded = """
            public sealed class GoodHandler(ICurrentTenant tenant) : IQueryHandler<GoodQuery, int>
            {
                public Task<int> Handle(GoodQuery query, CancellationToken cancellationToken)
                {
                    PlatformAdminScope.Require(tenant);
                    return Task.FromResult(1);
                }
            }

            public sealed class BadHandler(ICurrentTenant tenant) : IQueryHandler<BadQuery, int>
            {
                public Task<int> Handle(BadQuery query, CancellationToken cancellationToken)
                {
                    var x = 1;
                    PlatformAdminScope.Require(tenant);
                    return Task.FromResult(x);
                }
            }
            """;

        Assert.True(HandlerBodies([Guarded], "GoodHandler").All(FirstStatementIsGuard));
        Assert.False(HandlerBodies([Guarded], "BadHandler").All(FirstStatementIsGuard));
        Assert.Empty(HandlerBodies([Guarded], "MissingHandler"));
    }

    private static IEnumerable<Type> HandlersOfMarkedRequests() =>
        SolutionAssemblies.ApplicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Namespace?.StartsWith(ModuleNamespace, StringComparison.Ordinal) == true)
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && (contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) || contract.GetGenericTypeDefinition() == typeof(IRequestHandler<>))
                && typeof(IPlatformAdministrationRequest).IsAssignableFrom(contract.GetGenericArguments()[0])));

    /// <summary>The text of every <c>Handle</c> method of the named class (from its declaration to the next top-level type).</summary>
    private static IEnumerable<string> HandlerBodies(IEnumerable<string> sources, string className)
    {
        foreach (var source in sources)
        {
            var start = source.IndexOf($"class {className}(", StringComparison.Ordinal);
            if (start < 0)
            {
                start = source.IndexOf($"class {className} ", StringComparison.Ordinal);
            }

            if (start < 0)
            {
                continue;
            }

            var end = source.IndexOf("\npublic ", start + 1, StringComparison.Ordinal);
            var classText = end < 0 ? source[start..] : source[start..end];
            var handle = classText.IndexOf(" Handle(", StringComparison.Ordinal);
            if (handle >= 0)
            {
                yield return classText[handle..];
            }
        }
    }

    /// <summary>The first statement of the method body (after the opening brace, or after <c>=&gt;</c>) is the guard.</summary>
    private static bool FirstStatementIsGuard(string handleText)
    {
        var brace = handleText.IndexOf('{', StringComparison.Ordinal);
        var arrow = handleText.IndexOf("=>", StringComparison.Ordinal);
        var bodyStart = arrow >= 0 && (brace < 0 || arrow < brace) ? arrow + 2 : brace + 1;
        return handleText[bodyStart..].TrimStart().StartsWith(Guard, StringComparison.Ordinal);
    }
}
