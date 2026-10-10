using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Patients;

/// <summary>
/// A document attached to a patient file (CP-11 "attach documents"; visible from every branch). Its own aggregate:
/// a file's documents grow without bound and change independently of the demographics (03 §3 step 4). The content
/// lives in the file store under a server-generated key; the row keeps the display name, the detected content type,
/// the size and the SHA-256 of the content. Never deleted (BR-15): T5 deactivates it with a reason.
/// </summary>
public sealed class PatientDocument : AggregateRoot, ITenantScoped
{
    public const int MaxContentTypeLength = 100;
    public const int MaxStorageKeyLength = 400;
    public const int Sha256HexLength = 64;
    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 1000;

    private PatientDocument()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        StorageKey = string.Empty;
        Sha256 = string.Empty;
    }

    private PatientDocument(Guid id, Guid patientId, DocumentFile file, string storageKey, string sha256, string? note, Guid uploadedByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        PatientId = patientId;
        FileName = file.FileName;
        ContentType = file.ContentType;
        SizeBytes = file.SizeBytes;
        StorageKey = storageKey;
        Sha256 = sha256;
        Note = note;
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = nowUtc;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public Guid PatientId { get; private set; }

    /// <summary>The client's file name, cleaned, for display and download only (never part of a path).</summary>
    public string FileName { get; private set; }

    /// <summary>The detected content type (application/pdf, image/jpeg, image/png).</summary>
    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Opaque key of the file store (<c>&lt;tenant&gt;/patients/&lt;patient&gt;/&lt;guid&gt;</c>).</summary>
    public string StorageKey { get; private set; }

    /// <summary>SHA-256 of the stored content, lower-case hex.</summary>
    public string Sha256 { get; private set; }

    public string? Note { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public Guid? DeactivatedByUserId { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    /// <summary>Required on T5 (INV-PAT-009).</summary>
    public string? DeactivationReason { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// T4 Attach (<c>files.create</c>): the file must be Active (PAT-020); the content was accepted by
    /// <see cref="PatientDocumentFiles.Inspect"/> before it was stored; note ≤ 500 (PAT-045).
    /// </summary>
    public static PatientDocument Attach(Patient patient, DocumentFile file, string storageKey, string sha256, string? note, Guid uploadedByUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(storageKey.Length, MaxStorageKeyLength, nameof(storageKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        patient.RequireActive("attach a document");
        Require(file.SizeBytes is > 0 and <= PatientDocumentFiles.MaxSizeBytes, PatientCodes.DocumentTooLarge, "The document size is not allowed.", DomainFault.Validation);

        var cleanedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Require(cleanedNote is null || cleanedNote.Length <= MaxNoteLength, PatientCodes.DocumentNoteTooLong, $"The note may not exceed {MaxNoteLength} characters.", DomainFault.Validation);

        var document = new PatientDocument(Guid.CreateVersion7(), patient.Id, file, storageKey, sha256.ToLowerInvariant(), cleanedNote, uploadedByUserId, nowUtc);
        document.Raise(new PatientDocumentAttached(document.Id, document.PatientId, document.ContentType, document.SizeBytes, nowUtc));
        return document;
    }

    /// <summary>T5 Deactivate (<c>files.manage</c>): Active only (PAT-046); reason required (PAT-047). Terminal; row and content are kept.</summary>
    public void Deactivate(Guid actorUserId, DateTimeOffset nowUtc, string reason)
    {
        if (!IsActive)
        {
            throw new InvalidStateTransitionException(PatientCodes.DocumentInactive, "patient document", "Inactive", "deactivate");
        }

        var cleaned = (reason ?? string.Empty).Trim();
        Require(cleaned.Length is >= 1 and <= MaxReasonLength, PatientCodes.DeactivationReasonRequired, $"A deactivation needs a reason of 1 to {MaxReasonLength} characters.", DomainFault.Validation);

        IsActive = false;
        DeactivatedByUserId = actorUserId;
        DeactivatedAtUtc = nowUtc;
        DeactivationReason = cleaned;
        Raise(new PatientDocumentDeactivated(Id, PatientId, actorUserId, cleaned, nowUtc));
    }
}
