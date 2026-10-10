using Platform.SharedKernel.Primitives;

namespace Platform.SharedKernel.Results;

/// <summary>A structured refusal: a per-module code, the message the user sees and the fault kind that selects the HTTP status.</summary>
public sealed record Error(string Code, string Message, DomainFault Fault = DomainFault.Refused)
{
    public static readonly Error None = new(string.Empty, string.Empty, DomainFault.Unclassified);

    public static Error Validation(string code, string message) => new(code, message, DomainFault.Validation);

    public static Error Conflict(string code, string message) => new(code, message, DomainFault.Conflict);

    public static Error NotFound(string code, string message) => new(code, message, DomainFault.NotFound);

    public static Error Refused(string code, string message) => new(code, message, DomainFault.Refused);

    public static Error Unauthenticated(string code, string message) => new(code, message, DomainFault.Unauthenticated);

    /// <summary>The exception form of this error, for the handler boundary where refusals are thrown (01 §2.3).</summary>
    public DomainException ToException() => new(Code, Message, Fault);
}
