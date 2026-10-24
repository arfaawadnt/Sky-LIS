using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;

namespace Platform.Application.PlatformAdmin;

// Silent clients (change request LIS-CR-007 A-01): the SA-03 tile and the SA-04 "Silent" filter. The rule is
// Domain.PlatformAdmin.ClientSilence; the reader brings the timestamps of the platform tables (saas.tenants,
// identity.refresh_sessions: no RLS) and the last registration of a client from its own tenant scope.

/// <summary>
/// A Trial or Active client and the latest browser session issued to one of its users (sign-in or renewal; machine
/// sessions of site agents do not count). <paramref name="AdministratorName"/> / <paramref name="AdministratorMobile"/>
/// are the client record's manager fields (E.164).
/// </summary>
public sealed record ClientActivityRow(
    Guid TenantId,
    string NameEn,
    string NameAr,
    string PortalSlug,
    TenantStatus Status,
    DateTimeOffset CreatedAtUtc,
    string AdministratorName,
    string AdministratorMobile,
    DateTimeOffset? LastSignInUtc);

/// <summary>The reads behind <see cref="SilentClientFinder"/>; the adapter never elevates.</summary>
public interface IClientLastActivityReader
{
    /// <summary>Every client that is not Inactive with its last browser sign-in (the platform tenant is not a client).</summary>
    Task<IReadOnlyList<ClientActivityRow>> ListClientsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The latest <c>registered_at_utc</c> of the given clients' visits (a client without visits is absent). Each client
    /// is read in its own tenant scope (RLS), so the call costs one indexed query per client: callers pass only the
    /// clients the sign-ins did not already clear.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastRegistrationsAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken);
}

/// <summary>The silent clients of a window, shared by the dashboard, the list and the export.</summary>
public static class SilentClientFinder
{
    /// <summary>
    /// The silent clients as of <paramref name="nowUtc"/>, longest silence first (no activity at all before any date, then
    /// the oldest activity, then the oldest client, then the name). A client with a sign-in inside the window is cleared
    /// without reading its registrations (it cannot be silent, whatever it registered).
    /// </summary>
    public static async Task<IReadOnlyList<SilentClientItem>> FindAsync(IClientLastActivityReader reader, string portalBaseUrl, DateTimeOffset nowUtc, int days, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _ = ClientSilence.Cutoff(nowUtc, days);   // refuses a window outside 1…90 before any read
        var clients = await reader.ListClientsAsync(cancellationToken).ConfigureAwait(false);
        var candidates = clients
            .Where(client => ClientSilence.IsSilent(client.Status, client.CreatedAtUtc, client.LastSignInUtc, nowUtc, days))
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var registrations = await reader.LastRegistrationsAsync(candidates.Select(client => client.TenantId).ToList(), cancellationToken).ConfigureAwait(false);
        var silent = new List<SilentClientItem>();
        foreach (var client in candidates)
        {
            DateTimeOffset? registration = registrations.TryGetValue(client.TenantId, out var at) ? at : null;
            var last = ClientSilence.LastActivity(client.LastSignInUtc, registration);
            if (!ClientSilence.IsSilent(client.Status, client.CreatedAtUtc, last, nowUtc, days))
            {
                continue;
            }

            silent.Add(new SilentClientItem(
                client.TenantId,
                client.NameEn,
                client.NameAr,
                Tenant.PortalUrlOf(client.PortalSlug, portalBaseUrl),
                client.Status.ToString(),
                client.CreatedAtUtc,
                client.LastSignInUtc,
                registration,
                last,
                ClientSilence.DaysSilent(client.CreatedAtUtc, last, nowUtc),
                client.AdministratorName,
                client.AdministratorMobile));
        }

        return silent
            .OrderBy(item => item.LastActivityAtUtc.HasValue)
            .ThenBy(item => item.LastActivityAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.NameEn, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
