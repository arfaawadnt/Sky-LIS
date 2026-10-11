using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Platform.Contracts.Common;
using Platform.Infrastructure.Security;
using Platform.SharedKernel.Primitives;

namespace Platform.WebApi.Middleware;

/// <summary>
/// Maps refusals to status codes by their fault kind (01 §5.1): Validation 422, Conflict 409, NotFound 404,
/// Refused 403, Unauthenticated 401, Unclassified 400; FluentValidation failures 400 with <c>errors</c> (the messages,
/// unchanged) and <c>rules</c> (per field, the machine-readable rule that failed and its numeric arguments, e.g.
/// <c>{ "rule": "ExactLength", "minLength": 3, "maxLength": 3 }</c>), so a client can show its own localized text
/// instead of the validator's English (C-06). Anything else is a 500 with a code and no detail.
/// </summary>
public sealed partial class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public const string UnexpectedCode = "INTERNAL-500";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case DomainException domain:
                await ProblemResponse.WriteAsync(httpContext, StatusOf(domain.Fault), domain.Message, domain.Code);
                return true;

            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                    .ToDictionary(group => CamelCase(group.Key), group => group.Select(failure => failure.ErrorMessage).ToArray(), StringComparer.Ordinal);
                var rules = validation.Errors
                    .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                    .ToDictionary(group => CamelCase(group.Key), group => group.Select(RuleOf).ToArray(), StringComparer.Ordinal);
                await ProblemResponse.WriteAsync(httpContext, StatusCodes.Status400BadRequest, "The request is not valid.", ProblemCodes.ValidationFailed, errors, rules);
                return true;

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                return true;

            default:
                LogUnexpected(logger, exception, httpContext.TraceIdentifier);
                await ProblemResponse.WriteAsync(httpContext, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", UnexpectedCode);
                return true;
        }
    }

    public static int StatusOf(DomainFault fault) => fault switch
    {
        DomainFault.Validation => StatusCodes.Status422UnprocessableEntity,
        DomainFault.Conflict => StatusCodes.Status409Conflict,
        DomainFault.NotFound => StatusCodes.Status404NotFound,
        DomainFault.Refused => StatusCodes.Status403Forbidden,
        DomainFault.Unauthenticated => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>The validator's arguments a message may quote; only numbers, never a value taken from the request.</summary>
    private static readonly (string Placeholder, string Name)[] RuleArguments =
    [
        ("MinLength", "minLength"),
        ("MaxLength", "maxLength"),
        ("ComparisonValue", "comparisonValue"),
        ("From", "from"),
        ("To", "to"),
        ("ExpectedPrecision", "precision"),
        ("ExpectedScale", "scale"),
    ];

    /// <summary>
    /// <c>NotEmptyValidator</c> → <c>{ "rule": "NotEmpty" }</c>; a code set with <c>WithErrorCode</c> is kept as it is.
    /// </summary>
    private static IReadOnlyDictionary<string, object> RuleOf(ValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var code = string.IsNullOrWhiteSpace(failure.ErrorCode) ? "Invalid" : failure.ErrorCode;
        var rule = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rule"] = code.EndsWith("Validator", StringComparison.Ordinal) && code.Length > "Validator".Length ? code[..^"Validator".Length] : code,
        };

        if (failure.FormattedMessagePlaceholderValues is { } values)
        {
            foreach (var (placeholder, name) in RuleArguments)
            {
                if (values.TryGetValue(placeholder, out var value) && IsFiniteNumber(value))
                {
                    rule[name] = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                }
            }
        }

        return rule;
    }

    private static bool IsFiniteNumber(object? value) => value switch
    {
        byte or short or int or long or decimal => true,
        float single => float.IsFinite(single),
        double number => double.IsFinite(number),
        _ => false,
    };

    private static string CamelCase(string name) => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    [LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "unhandled exception for request {TraceId}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string traceId);
}

/// <summary>Echoes or mints a correlation id (01 §5.2); a client value is kept only when it is short and plain. The id, the client address and the user-agent hash are handed to the scoped request context the audit rows read.</summary>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";
    public const string CorrelationItem = "platform.correlation-id";

    public Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[Header].ToString();
        var id = supplied.Length is > 0 and <= 64 && supplied.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? supplied
            : Guid.CreateVersion7().ToString("N");

        context.Items[CorrelationItem] = id;
        context.RequestServices.GetRequiredService<RequestContextAccessor>().Set(id, context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString());
        context.Response.OnStarting(static state =>
        {
            var (response, value) = ((HttpResponse, string))state;
            response.Headers[Header] = value;   // set at send time so an exception handler or a challenge cannot clear it
            return Task.CompletedTask;
        }, (context.Response, id));
        return next(context);
    }
}
