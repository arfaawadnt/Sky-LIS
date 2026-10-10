using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Domain.Patients;

namespace Platform.Application.Patients;

/// <summary>What the register filters on; <see cref="Terms"/> is the parsed search box (<see cref="PatientSearchTerms.Parse"/>).</summary>
public sealed record PatientSearchFilter(PatientSearchTerms? Terms, PatientGender? Gender, bool? IsVip, PatientStatus? Status);

/// <summary>
/// One flat projection of a patient file: every register, detail, export, similar candidate and lookup is built
/// from it by <see cref="PatientProjections"/> (age for today, VIP masking), so the rules live in one place.
/// </summary>
public sealed record PatientRow(
    Guid Id,
    string Number,
    PatientTitle Title,
    string FullName,
    string? NameAr,
    string NameKey,
    string NameArKey,
    PatientGender Gender,
    DateOnly? DateOfBirth,
    int? AgeYears,
    int? AgeMonths,
    int? AgeDays,
    DateOnly? AgeRecordedOn,
    DateOnly RegisteredOn,
    Guid? PatientConditionId,
    string? Mobile,
    string? NationalId,
    string? Nationality,
    string? Email,
    Guid? GovernorateId,
    string? City,
    string? Address,
    string? Comments,
    bool IsVip,
    PatientStatus Status,
    Guid? MergedIntoPatientId,
    string? MergedIntoNumber,
    DateTimeOffset? MergedAtUtc,
    Guid? MergedByUserId,
    string? MergeReason,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByName,
    DateTimeOffset? ModifiedAtUtc,
    string? ModifiedByName,
    uint RowVersion);

/// <summary>Write side of the patient file plus the projections its queries need. Every read is bounded to the current tenant.</summary>
public interface IPatientRepository : IRepository<Patient>
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page, newest registration first.</summary>
    Task<Paged<PatientRow>> SearchAsync(PatientSearchFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="limit"/> rows of the filter, newest first (exports).</summary>
    Task<IReadOnlyList<PatientRow>> SearchAllAsync(PatientSearchFilter filter, int limit, CancellationToken cancellationToken);

    Task<PatientRow?> GetRowAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PatientRow>> GetRowsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The files merged directly into <paramref name="survivorId"/>.</summary>
    Task<IReadOnlyList<MergedPatientReference>> ListMergedIntoAsync(Guid survivorId, CancellationToken cancellationToken);

    /// <summary><paramref name="survivorId"/> and every file merged into it, transitively (documents of absorbed files stay visible).</summary>
    Task<IReadOnlyList<Guid>> MergedFamilyAsync(Guid survivorId, CancellationToken cancellationToken);

    /// <summary>
    /// Active files that may be similar (BR-01): the same normalised mobile, or a stored name key that contains at
    /// least two of <paramref name="nameTokens"/> as whole tokens (one token: the whole key). A coarse pre-filter; the
    /// handler decides the match with <see cref="PatientNameNormalizer.KeysMatch"/>.
    /// </summary>
    Task<IReadOnlyList<PatientRow>> FindSimilarCandidatesAsync(IReadOnlyList<string> nameTokens, string? mobile, int limit, CancellationToken cancellationToken);
}

/// <summary>Write side of patient documents plus their register.</summary>
public interface IPatientDocumentRepository : IRepository<PatientDocument>
{
    /// <summary>Documents of the given files (a survivor and the files merged into it), newest first.</summary>
    Task<IReadOnlyList<PatientDocumentItem>> ListAsync(IReadOnlyCollection<Guid> patientIds, bool includeInactive, CancellationToken cancellationToken);

    Task<PatientDocumentItem?> GetItemAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>The storage key of a document, for streaming its content.</summary>
    Task<string?> GetStorageKeyAsync(Guid documentId, CancellationToken cancellationToken);
}
