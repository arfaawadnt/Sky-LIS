using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.CredentialTransport;
using Platform.Contracts.Common;
using Platform.WebApi.Middleware;

namespace Platform.WebApi.Security;

/// <summary>
/// Opens every <c>[CredentialField]</c> value of every bound argument before the action runs (02 §3.6, D-03):
/// the only code that knows a field was enveloped. Scope comes from the attribute, never from a list; there is no
/// optional mode and no environment where a plaintext credential is accepted (C-07). Refusals are <c>400 CRED-nnn</c>
/// and never count toward a lockout: no credential was checked.
/// </summary>
public sealed class CredentialTransportFilter(ICredentialEnvelopeOpener opener, ISecurityEventLog events) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var audience = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
        var requestNonces = new HashSet<string>(StringComparer.Ordinal);   // one challenge per request, shared by its fields

        foreach (var (argument, property) in CredentialFieldMap.For(context.ActionDescriptor))
        {
            if (!context.ActionArguments.TryGetValue(argument, out var model) || model is null)
            {
                continue;
            }

            var value = (string?)property.GetValue(model);
            if (value is null)
            {
                continue;
            }

            var result = await opener.OpenAsync(value, CamelCase(property.Name), audience, requestNonces, context.HttpContext.RequestAborted);
            if (!result.Ok)
            {
                await events.WriteAsync(new SecurityEvent(SecurityEvents.CredentialEnvelopeRejected, SecurityEvent.Warning, Detail: result.Code), context.HttpContext.RequestAborted);
                context.Result = ProblemResponse.Result(context.HttpContext, StatusCodes.Status400BadRequest, result.Title, result.Code);
                return;
            }

            property.SetValue(model, result.Plaintext);
        }

        await next();
    }

    private static string CamelCase(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

/// <summary>Per-action map of bound arguments to their <c>[CredentialField]</c> properties, built once by reflection.</summary>
public static class CredentialFieldMap
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<(string Argument, PropertyInfo Property)>> Cache = new(StringComparer.Ordinal);

    public static IReadOnlyList<(string Argument, PropertyInfo Property)> For(ActionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Cache.GetOrAdd(descriptor.Id, _ => Build(descriptor));
    }

    public static IReadOnlyList<PropertyInfo> CredentialPropertiesOf(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(string) && property.GetCustomAttribute<CredentialFieldAttribute>(inherit: true) is not null && property.CanWrite)
            .ToList();

    private static IReadOnlyList<(string, PropertyInfo)> Build(ActionDescriptor descriptor)
    {
        if (descriptor is not ControllerActionDescriptor controllerAction)
        {
            return [];
        }

        return controllerAction.Parameters
            .SelectMany(parameter => CredentialPropertiesOf(parameter.ParameterType).Select(property => (parameter.Name, property)))
            .ToList();
    }
}
