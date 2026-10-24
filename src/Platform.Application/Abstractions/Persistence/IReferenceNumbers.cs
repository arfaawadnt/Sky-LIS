namespace Platform.Application.Abstractions.Persistence;

/// <summary>
/// Reference numbers from a per-tenant, per-kind, per-year counter updated atomically (pitfall P-12): never
/// <c>count + 1</c>, never reused, gaps allowed. Format <c>&lt;prefix&gt;-&lt;yyyy&gt;-&lt;nnnnn&gt;</c>.
/// </summary>
public interface IReferenceNumbers
{
    Task<string> NextAsync(string kind, string prefix, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// A short code from a per-tenant, per-kind counter that never restarts (no year), formatted by
    /// <see cref="Platform.SharedKernel.Primitives.ReferenceNumberFormat.Short"/>: <c>&lt;prefix&gt;-&lt;n&gt;</c> with at least
    /// <paramref name="width"/> digits, e.g. <c>DR-001</c> (LIS-CR-007 C-09). Same atomic counter table as <see cref="NextAsync"/>.
    /// </summary>
    Task<string> NextShortAsync(string kind, string prefix, int width, CancellationToken cancellationToken);
}
