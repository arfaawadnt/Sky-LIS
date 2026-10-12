using Microsoft.EntityFrameworkCore;
using Platform.Application.Identity;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence.Identity;

/// <summary>Platform users are never listed; every read is keyed by id or sign-in name (D-06).</summary>
internal sealed class PlatformUserRepository(AppDbContext context) : Repository<PlatformUser>(context), IPlatformUserRepository
{
    public Task<PlatformUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var normalised = Normalise(username);
        return Set.FirstOrDefaultAsync(user => user.Username == normalised, cancellationToken);
    }

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken)
    {
        var normalised = Normalise(username);
        return Set.AnyAsync(user => user.Username == normalised, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var normalised = Normalise(email);
        return Set.AnyAsync(user => user.Email == normalised, cancellationToken);
    }

    public Task<PlatformUser?> FindByMobileAsync(string mobile, CancellationToken cancellationToken)
    {
        var normalised = PlatformUser.NormaliseMobile(mobile);
        return normalised is null ? Task.FromResult<PlatformUser?>(null) : Set.FirstOrDefaultAsync(user => user.Mobile == normalised, cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformUser>> ListByMobileSpellingsAsync(IReadOnlyCollection<string> spellings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spellings);
        if (spellings.Count == 0)
        {
            return [];
        }

        var values = spellings.Distinct(StringComparer.Ordinal).ToList();
        return await Set.Where(user => user.Mobile != null && values.Contains(user.Mobile)).Take(10).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Normalise(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>Sessions are read by id, token hash, family or user (02 §4.3).</summary>
internal sealed class RefreshSessionRepository(AppDbContext context) : Repository<RefreshSession>(context), IRefreshSessionRepository
{
    public Task<RefreshSession?> FindByTokenHashAsync(byte[] refreshTokenHash, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(session => session.RefreshTokenHash == refreshTokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshSession>> ListFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        await Set.Where(session => session.FamilyId == familyId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<RefreshSession>> ListLiveForUserAsync(Guid platformUserId, CancellationToken cancellationToken) =>
        await Set.Where(session => session.PlatformUserId == platformUserId && !session.IsRevoked && session.RotatedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<RefreshSession?> FindCommittedAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Set.AsNoTracking().FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
}
