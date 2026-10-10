using System.Reflection;
using MediatR;
using Platform.Application.Abstractions.Security;
using Platform.Application.Behaviors;
using Platform.Domain.Authorization;

namespace Platform.ArchitectureTests.Support;

/// <summary>
/// The rules behind RequestPolicyTests (D-04, 01 §8): every command and query carries exactly one policy
/// attribute, every permission key is catalogued, every handler handles a gated request, no role attribute exists.
/// </summary>
internal static class RequestPolicyGate
{
    /// <summary>Concrete types implementing <c>ICommand</c>, <c>ICommand&lt;T&gt;</c> or <c>IQuery&lt;T&gt;</c>.</summary>
    public static IReadOnlyList<Type> RequestsIn(Assembly assembly) =>
        assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract && RequestGating.IsGated(type)).ToList();

    /// <summary>Requests carrying zero or more than one <see cref="RequestPolicyAttribute"/>.</summary>
    public static IReadOnlyList<string> MissingOrDuplicatePolicy(IEnumerable<Type> requests) =>
        requests.Select(type => (type, count: type.GetCustomAttributes(typeof(RequestPolicyAttribute), inherit: false).Length))
            .Where(entry => entry.count != 1)
            .Select(entry => $"{entry.type.FullName} ({entry.count} policy attributes)")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>Requests whose permission policy names a key outside <see cref="PermissionCatalog"/>.</summary>
    public static IReadOnlyList<string> UnknownPermissions(IEnumerable<Type> requests) =>
        requests.Select(type => (type, policy: type.GetCustomAttribute<RequirePermissionPolicyAttribute>(inherit: false)))
            .Where(entry => entry.policy is not null && !PermissionCatalog.IsKnown(entry.policy.PermissionKey))
            .Select(entry => $"{entry.type.FullName} -> '{entry.policy!.PermissionKey}'")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>MediatR handlers whose request is a raw <c>IRequest</c> rather than a gated command or query.</summary>
    public static IReadOnlyList<string> UngatedHandlers(Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .SelectMany(type => type.GetInterfaces()
                .Where(IsRequestHandlerInterface)
                .Select(handlerInterface => (type, request: handlerInterface.GetGenericArguments()[0])))
            .Where(entry => !RequestGating.IsGated(entry.request))
            .Select(entry => $"{entry.type.FullName} handles {entry.request.Name}, which is not an ICommand or IQuery")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>Attribute types whose name says "RequireRole": roles are tenant-configurable and never a policy (01 §3.4).</summary>
    public static IReadOnlyList<string> RoleAttributes(IEnumerable<Assembly> assemblies) =>
        assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(Attribute).IsAssignableFrom(type) && type.Name.Contains("RequireRole", StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static bool IsRequestHandlerInterface(Type candidate)
    {
        if (!candidate.IsGenericType)
        {
            return false;
        }

        var definition = candidate.GetGenericTypeDefinition();
        return definition == typeof(IRequestHandler<>) || definition == typeof(IRequestHandler<,>);
    }
}
