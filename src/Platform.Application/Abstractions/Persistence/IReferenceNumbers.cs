namespace Platform.Application.Abstractions.Persistence;

/// <summary>
/// Reference numbers from a per-tenant, per-kind, per-year counter updated atomically (pitfall P-12): never
/// <c>count + 1</c>, never reused, gaps allowed. Format <c>&lt;prefix&gt;-&lt;yyyy&gt;-&lt;nnnnn&gt;</c>.
/// </summary>
public interface IReferenceNumbers
{
    Task<string> NextAsync(string kind, string prefix, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
