namespace Platform.SharedKernel.Abstractions;

/// <summary>
/// The only source of time for domain and application code (01 §2.3, pitfall P-14). Tests substitute a fixed
/// clock; <see cref="SystemClock"/> is the single production implementation.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Reads the machine clock. The banned-symbols analyzer forbids this call anywhere else.</summary>
public sealed class SystemClock : IClock
{
#pragma warning disable RS0030 // the one sanctioned reader of the system clock
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
#pragma warning restore RS0030
}
