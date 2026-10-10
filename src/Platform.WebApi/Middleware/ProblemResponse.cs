using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Contracts.Common;

namespace Platform.WebApi.Middleware;

/// <summary>The one error shape (01 §5.1): <c>application/problem+json</c> with <c>code</c>, <c>traceId</c> and <c>correlationId</c>. Never a stack trace, never free text from the request.</summary>
public static class ProblemResponse
{
    public const string ContentType = "application/problem+json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, int status, string title, string code, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = ContentType;
        return context.Response.WriteAsync(JsonSerializer.Serialize(Body(context, status, title, code, errors), Json), context.RequestAborted);
    }

    public static IActionResult Result(HttpContext context, int status, string title, string code, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new ObjectResult(Body(context, status, title, code, errors)) { StatusCode = status, ContentTypes = { ContentType } };

    private static Dictionary<string, object?> Body(HttpContext context, int status, string title, string code, IReadOnlyDictionary<string, string[]>? errors)
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

        return body;
    }

    public static IActionResult InvalidModelState(ActionContext actionContext)
    {
        ArgumentNullException.ThrowIfNull(actionContext);
        var errors = actionContext.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? "invalid" : error.ErrorMessage).ToArray(), StringComparer.Ordinal);
        return Result(actionContext.HttpContext, StatusCodes.Status400BadRequest, "The request is not valid.", ProblemCodes.ValidationFailed, errors);
    }
}
