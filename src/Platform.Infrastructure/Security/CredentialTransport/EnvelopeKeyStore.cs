using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Security.Protection;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Security.CredentialTransport;

/// <summary>The decrypted, in-memory form of an envelope key. The RSA object is created per use; PKCS#8 bytes are kept.</summary>
public sealed record EnvelopeKeyMaterial(string Kid, byte[] PrivateKeyPkcs8, string PublicKeyPem, byte[] NonceMacKey, DateTimeOffset CreatedAtUtc, DateTimeOffset? RetiredAtUtc)
{
    public RSA OpenRsa()
    {
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(PrivateKeyPkcs8, out _);
        return rsa;
    }
}

/// <summary>
/// Shared key store (C-08, pitfall P-41): keys live in <c>identity.envelope_keys</c>, so every replica accepts the
/// same challenges and a restart invalidates nothing. The first caller after the rotation interval creates the next
/// key under a transaction-scoped advisory lock and retires the previous one. A process-local cache avoids a read
/// per request; what it caches is derivable from the table, so it bears no correctness.
/// </summary>
public sealed class EnvelopeKeyStore(IServiceScopeFactory scopes, MasterKeyProtector protector, SecurityOptions options, IClock clock)
{
    private const string LockName = "platform:envelope_keys";
    private readonly ConcurrentDictionary<string, EnvelopeKeyMaterial> _byKid = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _rotation = new(1, 1);
    private EnvelopeKeyMaterial? _active;

    public async Task<EnvelopeKeyMaterial> GetActiveAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (_active is { } cached && cached.RetiredAtUtc is null && cached.CreatedAtUtc + options.EnvelopeKeyRotation > now)
        {
            return cached;
        }

        await _rotation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active is { } again && again.RetiredAtUtc is null && again.CreatedAtUtc + options.EnvelopeKeyRotation > now)
            {
                return again;
            }

            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({LockName}))", cancellationToken).ConfigureAwait(false);

            var newest = await context.EnvelopeKeys
                .Where(key => key.RetiredAtUtc == null)
                .OrderByDescending(key => key.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            EnvelopeKeyMaterial? material = null;
            if (newest is not null && newest.CreatedAtUtc + options.EnvelopeKeyRotation > now)
            {
                material = TryDecrypt(newest);
                if (material is null)
                {
                    newest.Retire(now);   // sealed under a master key this process does not hold (rotation runbook): unusable, replaced
                }
            }
            else
            {
                newest?.Retire(now);
            }

            if (material is null)
            {
                var created = Generate(now);
                context.EnvelopeKeys.Add(created);
                material = Decrypt(created);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _byKid[material.Kid] = material;
            _active = material;
            return material;
        }
        finally
        {
            _rotation.Release();
        }
    }

    /// <summary>The key a header names, when it is the active key or one retired within the overlap; otherwise <see langword="null"/>.</summary>
    public async Task<EnvelopeKeyMaterial?> FindAsync(string kid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kid) || kid.Length > EnvelopeKey.MaxKidLength)
        {
            return null;
        }

        var now = clock.UtcNow;
        if (_byKid.TryGetValue(kid, out var cached) && IsAccepted(cached, now))
        {
            return cached;
        }

        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await context.EnvelopeKeys.AsNoTracking().FirstOrDefaultAsync(key => key.Kid == kid, cancellationToken).ConfigureAwait(false);
        if (row is null || !row.IsAcceptedAt(now, options.EnvelopeKeyOverlap))
        {
            return null;
        }

        var material = TryDecrypt(row);
        if (material is not null)
        {
            _byKid[material.Kid] = material;
        }

        return material;
    }

    private bool IsAccepted(EnvelopeKeyMaterial material, DateTimeOffset now) =>
        material.RetiredAtUtc is null ? material.CreatedAtUtc + options.EnvelopeKeyRotation + options.EnvelopeKeyOverlap > now : material.RetiredAtUtc.Value + options.EnvelopeKeyOverlap > now;

    private EnvelopeKey Generate(DateTimeOffset now)
    {
        using var rsa = RSA.Create(options.EnvelopeRsaKeySize);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();
        var macKey = RandomNumberGenerator.GetBytes(SecurityOptions.KeyLength);
        var kid = $"{now:yyyy-MM-ddTHH:mm:ssZ}#{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";

        return new EnvelopeKey(
            kid,
            rsa.ExportSubjectPublicKeyInfoPem(),
            protector.Protect(EnvelopeKey.KeyProtectionPurpose, pkcs8),
            protector.Protect(EnvelopeKey.MacProtectionPurpose, macKey),
            now);
    }

    private EnvelopeKeyMaterial? TryDecrypt(EnvelopeKey row)
    {
        try
        {
            return Decrypt(row);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private EnvelopeKeyMaterial Decrypt(EnvelopeKey row) => new(
        row.Kid,
        protector.Unprotect(EnvelopeKey.KeyProtectionPurpose, row.PrivateKeyEnc),
        row.PublicKeyPem,
        protector.Unprotect(EnvelopeKey.MacProtectionPurpose, row.NonceMacKeyEnc),
        row.CreatedAtUtc,
        row.RetiredAtUtc);
}

/// <summary>The replay set of 02 §3.3: insert equals consume; the primary key refuses the second use.</summary>
public sealed class NonceStore(AppDbContext context)
{
    public async Task<bool> TryConsumeAsync(byte[] nonceHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nonceHash);
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.credential_nonces (nonce_hash, expires_at_utc) VALUES ({nonceHash}, {expiresAtUtc}) ON CONFLICT DO NOTHING",
            cancellationToken).ConfigureAwait(false);
        return inserted == 1;
    }

    /// <summary>Retention (01 §7): expired nonces are deleted hourly.</summary>
    public Task<int> PurgeExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.credential_nonces WHERE expires_at_utc < {nowUtc}", cancellationToken);
}
