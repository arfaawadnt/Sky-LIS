using Npgsql;

namespace Platform.WebApi.FunctionalTests.Support;

/// <summary>
/// LIS-CR-005: where a sign-in is made — a laboratory address (<c>{ laboratory: slug }</c>), the Super Admin sign-in
/// (<c>{ admin: true }</c>), or, for shape tests, neither or both.
/// </summary>
internal sealed record SignInTarget(string? Laboratory, bool? Admin)
{
    public static SignInTarget Administration { get; } = new(null, true);

    /// <summary>Neither target: the request is refused 400 before any credential check.</summary>
    public static SignInTarget None { get; } = new(null, null);

    public static SignInTarget At(string laboratory) => new(laboratory, null);
}

/// <summary>
/// LIS-CR-005: the sign-in target a test means when it names none — read from the database like the SPA reads it from the
/// address bar: the user's one active membership decides (the platform tenant → the Super Admin sign-in, a laboratory → its
/// address). An unknown account, or one without an active membership, goes to the Super Admin sign-in, where it is refused
/// <c>AUTH-002</c> exactly as at any laboratory address. Several memberships are ambiguous: such a test passes its target.
/// Reads as the owner role (BYPASSRLS), test support only.
/// </summary>
internal static class SignInTargets
{
    public static async Task<SignInTarget> ResolveAsync(string username)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select m.tenant_id, t.portal_slug
            from identity.platform_users u
            join app.tenant_memberships m on m.platform_user_id = u.id and m.is_active
            left join saas.tenants t on t.id = m.tenant_id
            where u.username = lower(trim(@username))
            """,
            connection);
        command.Parameters.AddWithValue("username", username ?? string.Empty);
        var rows = new List<(Guid TenantId, string? Slug)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        return rows switch
        {
            [] => SignInTarget.Administration,
            [var only] when Platform.SharedKernel.MultiTenancy.TenantIds.IsPlatform(only.TenantId) => SignInTarget.Administration,
            [var only] => SignInTarget.At(only.Slug ?? throw new InvalidOperationException($"Tenant {only.TenantId} has no client record, so no laboratory address; seed it through ApiFactory.SeedUserAsync or ApiFactory.EnsureClientRecordAsync.")),
            _ => throw new InvalidOperationException($"User '{username}' has {rows.Count} active memberships; pass the sign-in target explicitly."),
        };
    }
}
