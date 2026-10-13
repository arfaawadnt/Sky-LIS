using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Platform.Contracts.Common;
using Platform.WebApi.Security;

namespace Platform.WebApi.Middleware;

/// <summary>
/// The one error shape (01 §5.1): <c>application/problem+json</c> with <c>code</c>, <c>traceId</c> and <c>correlationId</c>;
/// a validation failure adds <c>errors</c> (field → messages) and <c>rules</c> (field → the rules that failed, C-06). Never a
/// stack trace, never free text from the request.
/// </summary>
public static class ProblemResponse
{
    public const string ContentType = "application/problem+json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, int status, string title, string code, IReadOnlyDictionary<string, string[]>? errors = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>[]>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = ContentType;
        return context.Response.WriteAsync(JsonSerializer.Serialize(Body(context, status, title, code, errors, rules), Json), context.RequestAborted);
    }

    public static IActionResult Result(HttpContext context, int status, string title, string code, IReadOnlyDictionary<string, string[]>? errors = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>[]>? rules = null) =>
        new ObjectResult(Body(context, status, title, code, errors, rules)) { StatusCode = status, ContentTypes = { ContentType } };

    private static Dictionary<string, object?> Body(HttpContext context, int status, string title, string code, IReadOnlyDictionary<string, string[]>? errors, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>[]>? rules)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
            ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
            ["correlationId"] = context.Items[CorrelationMiddleware.CorrelationItem] as string,
        };

        if (errors is { Count: > 0 })
        {
            body["errors"] = errors;
        }

        if (rules is { Count: > 0 })
        {
            body["rules"] = rules;
        }

        return body;
    }

    private static readonly IReadOnlyDictionary<string, object> FormatRule = new Dictionary<string, object>(StringComparer.Ordinal) { ["rule"] = "Format" };

    /// <summary>
    /// A body the binder could not read or that misses a required member: 400 with the <c>Format</c> rule per field, but only
    /// for a caller who may send the action's requests. Anyone else receives the pipeline's 401/403 first (validation F-17:
    /// authorization before validation on every path, so a refusal never discloses the request's rules).
    /// </summary>
    public static IActionResult InvalidModelState(ActionContext actionContext)
    {
        ArgumentNullException.ThrowIfNull(actionContext);
        if (actionContext.ActionDescriptor is ControllerActionDescriptor action)
        {
            ActionRequestPolicies.Demand(action, actionContext.HttpContext.RequestServices);   // throws the refusal; DomainExceptionHandler writes it
        }

        var errors = actionContext.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? "invalid" : error.ErrorMessage).ToArray(), StringComparer.Ordinal);
        // a body the binder could not read: the client shows its own "wrong format" text for these fields (C-06)
        var rules = errors.Keys.ToDictionary(key => key, _ => new IReadOnlyDictionary<string, object>[] { FormatRule }, StringComparer.Ordinal);
        return Result(actionContext.HttpContext, StatusCodes.Status400BadRequest, "The request is not valid.", ProblemCodes.ValidationFailed, errors, rules);
    }
}
