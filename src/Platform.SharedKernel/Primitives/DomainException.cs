namespace Platform.SharedKernel.Primitives;

/// <summary>
/// The kind of refusal, so the HTTP layer maps to a status without inferring semantics from the code's spelling
/// (01 §5.1): Validation 422, Conflict 409, NotFound 404, Refused 403, Unauthenticated 401, Unclassified 400.
/// </summary>
public enum DomainFault
{
    Unclassified = 0,
    Validation = 1,
    Conflict = 2,
    NotFound = 3,
    Refused = 4,
    Unauthenticated = 5,
}

/// <summary>
/// A coded refusal raised by an aggregate guard or a handler. The code (for example <c>REG-010</c>) is a
/// per-module constant; the message is what the user sees.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message, DomainFault fault = DomainFault.Refused)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Fault = fault;
    }

    public DomainException(string code, string message, DomainFault fault, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Fault = fault;
    }

    public string Code { get; }

    public DomainFault Fault { get; }
}

/// <summary>Raised by a guarded state machine when an action is not allowed in the current state.</summary>
public sealed class InvalidStateTransitionException(string code, string aggregate, string currentState, string action)
    : DomainException(code, $"'{action}' is not allowed while the {aggregate} is in state '{currentState}'.", DomainFault.Conflict)
{
    public string Aggregate { get; } = aggregate;

    public string CurrentState { get; } = currentState;

    public string Action { get; } = action;
}
