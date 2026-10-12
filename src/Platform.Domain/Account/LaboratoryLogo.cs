using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Account;

/// <summary>
/// The laboratory's logo (shown in the top bar of every user of the laboratory; user decision 2026-10-12: set on the
/// laboratory profile by a holder of <c>settings.manage</c>). One per tenant (unique index on the tenant): an upload
/// replaces the image of the same row and raises its <see cref="Version"/>, a removal clears it. The bytes live in the
/// file store under a server-generated key; earlier images stay there (BR-15: no business deletion of stored files).
/// </summary>
public sealed class LaboratoryLogo : AggregateRoot, ITenantScoped
{
    public const int MaxContentTypeLength = 50;
    public const int MaxStorageKeyLength = 400;
    public const int Sha256HexLength = 64;

    private LaboratoryLogo()
    {
        ContentType = string.Empty;
        StorageKey = string.Empty;
        Sha256 = string.Empty;
    }

    private LaboratoryLogo(Guid id) : base(id)
    {
        ContentType = string.Empty;
        StorageKey = string.Empty;
        Sha256 = string.Empty;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The detected content type (image/png, image/jpeg, image/webp).</summary>
    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Opaque key of the file store (<c>&lt;tenant&gt;/account/&lt;logo&gt;/&lt;guid&gt;</c>).</summary>
    public string StorageKey { get; private set; }

    /// <summary>SHA-256 of the stored content, lower-case hex.</summary>
    public string Sha256 { get; private set; }

    /// <summary>1 for the first image, +1 for every replacement or removal: clients reload the image when it changes.</summary>
    public int Version { get; private set; }

    /// <summary>False once removed: the laboratory shows its initials again.</summary>
    public bool IsActive { get; private set; }

    public Guid ChangedByUserId { get; private set; }

    public DateTimeOffset ChangedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>The first logo of the laboratory; the content was accepted by <see cref="LaboratoryLogoFiles.Inspect"/>.</summary>
    public static LaboratoryLogo Upload(LogoFile file, string storageKey, string sha256, Guid actorUserId, DateTimeOffset nowUtc)
    {
        var logo = new LaboratoryLogo(Guid.CreateVersion7());
        logo.Apply(file, storageKey, sha256, actorUserId, nowUtc);
        return logo;
    }

    /// <summary>A new image for the laboratory (also after a removal).</summary>
    public void Replace(LogoFile file, string storageKey, string sha256, Guid actorUserId, DateTimeOffset nowUtc) =>
        Apply(file, storageKey, sha256, actorUserId, nowUtc);

    /// <summary>Clears the logo (ACC-045 when there is none); the stored image is kept.</summary>
    public void Remove(Guid actorUserId, DateTimeOffset nowUtc)
    {
        Require(IsActive, AccountCodes.LogoNotFound, "The laboratory has no logo.", DomainFault.NotFound);
        IsActive = false;
        Version++;
        ChangedByUserId = actorUserId;
        ChangedAtUtc = nowUtc;
        Raise(new LaboratoryLogoRemoved(Id, Version, actorUserId, nowUtc));
    }

    private void Apply(LogoFile file, string storageKey, string sha256, Guid actorUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(storageKey.Length, MaxStorageKeyLength, nameof(storageKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        Require(file.SizeBytes is > 0 and <= LaboratoryLogoFiles.MaxSizeBytes, AccountCodes.LogoTooLarge, "The logo size is not allowed.", DomainFault.Validation);

        ContentType = file.ContentType;
        SizeBytes = file.SizeBytes;
        StorageKey = storageKey;
        Sha256 = sha256.ToLowerInvariant();
        IsActive = true;
        Version++;
        ChangedByUserId = actorUserId;
        ChangedAtUtc = nowUtc;
        Raise(new LaboratoryLogoChanged(Id, Version, ContentType, SizeBytes, actorUserId, nowUtc));
    }
}
