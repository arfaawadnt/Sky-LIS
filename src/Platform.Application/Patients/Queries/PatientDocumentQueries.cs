using FluentValidation;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Patients;
using Platform.Domain.Authorization;
using Platform.Domain.Patients;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Patients.Queries;

/// <summary>Documents of a file and of every file merged into it (provenance in <see cref="PatientDocumentItem.PatientNumber"/>), newest first.</summary>
[RequirePermissionPolicy(PermissionCatalog.Files, PermissionAction.View)]
public sealed record ListPatientDocumentsQuery(Guid PatientId, bool IncludeInactive) : IQuery<IReadOnlyList<PatientDocumentItem>>;

public sealed class ListPatientDocumentsValidator : AbstractValidator<ListPatientDocumentsQuery>
{
    public ListPatientDocumentsValidator() => RuleFor(query => query.PatientId).NotEmpty();
}

public sealed class ListPatientDocumentsHandler(IPatientRepository patients, IPatientDocumentRepository documents) : IQueryHandler<ListPatientDocumentsQuery, IReadOnlyList<PatientDocumentItem>>
{
    public async Task<IReadOnlyList<PatientDocumentItem>> Handle(ListPatientDocumentsQuery query, CancellationToken cancellationToken)
    {
        if (!await patients.ExistsAsync(query.PatientId, cancellationToken))
        {
            throw new DomainException(PatientCodes.PatientNotFound, "Patient file not found.", DomainFault.NotFound);
        }

        var family = await patients.MergedFamilyAsync(query.PatientId, cancellationToken);
        return await documents.ListAsync(family, query.IncludeInactive, cancellationToken);
    }
}

/// <summary>The bytes of a document, its detected content type and its display name; the caller disposes <see cref="Content"/>.</summary>
public sealed record PatientDocumentContent(Stream Content, string ContentType, string FileName, long SizeBytes);

/// <summary>Streams a document. An Inactive document's content needs <c>files.manage</c> (PAT-049); missing content is PAT-048.</summary>
[RequirePermissionPolicy(PermissionCatalog.Files, PermissionAction.View)]
public sealed record GetPatientDocumentContentQuery(Guid DocumentId) : IQuery<PatientDocumentContent>;

public sealed class GetPatientDocumentContentValidator : AbstractValidator<GetPatientDocumentContentQuery>
{
    public GetPatientDocumentContentValidator() => RuleFor(query => query.DocumentId).NotEmpty();
}

public sealed class GetPatientDocumentContentHandler(IPatientDocumentRepository documents, IFileStore files, IUserPrivileges privileges) : IQueryHandler<GetPatientDocumentContentQuery, PatientDocumentContent>
{
    public async Task<PatientDocumentContent> Handle(GetPatientDocumentContentQuery query, CancellationToken cancellationToken)
    {
        var document = await documents.GetItemAsync(query.DocumentId, cancellationToken)
            ?? throw new DomainException(PatientCodes.DocumentNotFound, "Document not found.", DomainFault.NotFound);
        if (!document.IsActive && !privileges.Has(PatientPermissions.ManageFiles))
        {
            throw new DomainException(PatientCodes.DocumentInactiveContent, "The document was deactivated; only a files manager can open it.", DomainFault.Refused);
        }

        var key = await documents.GetStorageKeyAsync(query.DocumentId, cancellationToken)
            ?? throw new DomainException(PatientCodes.DocumentNotFound, "Document not found.", DomainFault.NotFound);
        var content = await files.OpenReadAsync(key, cancellationToken)
            ?? throw new DomainException(PatientCodes.DocumentContentUnavailable, "The document content is not available.", DomainFault.NotFound);
        return new PatientDocumentContent(content, document.ContentType, document.FileName, document.SizeBytes);
    }
}
