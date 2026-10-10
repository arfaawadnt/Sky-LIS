using Platform.Application.Abstractions.Persistence;
using Platform.Domain.Identity;

namespace Platform.Application.Identity;

/// <summary>Write-side port of <see cref="PlatformUser"/>. Lookups are keyed by id or by the sign-in name; there is no cross-tenant listing.</summary>
public interface IPlatformUserRepository : IRepository<PlatformUser>
{
    Task<PlatformUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    /// <summary>The one user registered with this mobile (normalised form), or <see langword="null"/>; mobiles are unique among users that have one.</summary>
    Task<PlatformUser?> FindByMobileAsync(string mobile, CancellationToken cancellationToken);
}

/// <summary>Write-side port of <see cref="RefreshSession"/>. Lookups are keyed by session id, token hash or family.</summary>
public interface IRefreshSessionRepository : IRepository<RefreshSession>
{
    Task<RefreshSession?> FindByTokenHashAsync(byte[] refreshTokenHash, CancellationToken cancellationToken);

    /// <summary>Every session of a rotation family, for reuse detection (02 §4.3).</summary>
    Task<IReadOnlyList<RefreshSession>> ListFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    /// <summary>Every unrevoked session of a user, for password-change and deactivation revocation.</summary>
    Task<IReadOnlyList<RefreshSession>> ListLiveForUserAsync(Guid platformUserId, CancellationToken cancellationToken);
}
