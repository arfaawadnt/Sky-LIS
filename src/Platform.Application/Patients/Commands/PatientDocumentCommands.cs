using FluentValidation;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Patients;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Patients.Commands;

// Patient documents: T4 attach, T5 deactivate (BR-15: nothing is deleted).

/// <summary>
/// T4 Attach. <paramref name="Content"/> is the uploaded stream (the controller owns and disposes it);
/// <paramref name="FileName"/> is the client's name, for display only.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Files, PermissionAction.Create)]
public sealed record AttachPatientDocumentCommand(Guid PatientId, string? FileName, Stream Content, string? Note) : ICommand<CreatedResource>;

public sealed class AttachPatientDocumentValidator : AbstractValidator<AttachPatientDocumentCommand>
{
    public AttachPatientDocumentValidator()
    {
        RuleFor(command => command.PatientId).NotEmpty();
        RuleFor(command => command.FileName).NotEmpty().MaximumLength(1024);
        RuleFor(command => command.Content).NotNull();
        RuleFor(command => command.Note).MaximumLength(PatientDocument.MaxNoteLength);
    }
}

public sealed class AttachPatientDocumentHandler(
    IPatientRepository patients,
    IPatientDocumentRepository documents,
    IFileStore files,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<AttachPatientDocumentCommand, CreatedResource>
{
    /// <summary>The store's sub-folder of the module.</summary>
    public const string StorageArea = "patients";

    public async Task<CreatedResource> Handle(AttachPatientDocumentCommand command, CancellationToken cancellationToken)
    {
        var actor = PatientActor.Of(user);
        var patient = await patients.GetForUpdateAsync(command.PatientId, cancellationToken)
            ?? throw new DomainException(PatientCodes.PatientNotFound, "Patient file not found.", DomainFault.NotFound);
        patient.RequireActive("attach a document");

        // Buffer at most 10 MB + 1 byte: the size and the signature are decided before anything reaches the store.
        await using var buffer = new MemoryStream();
        await CopyBoundedAsync(command.Content, buffer, PatientDocumentFiles.MaxSizeBytes + 1, cancellationToken);
        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, PatientDocumentFiles.HeaderLength)).ToArray();
        var file = PatientDocumentFiles.Inspect(command.FileName, buffer.Length, header);

        buffer.Position = 0;
        var stored = await files.SaveAsync(StorageArea, patient.Id, buffer, PatientDocumentFiles.MaxSizeBytes, cancellationToken);
        try
        {
            var document = PatientDocument.Attach(patient, file, stored.StorageKey, stored.Sha256, command.Note, actor, clock.UtcNow);
            documents.Add(document);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new CreatedResource(document.Id);
        }
        catch
        {
            await files.DeleteAsync(stored.StorageKey, CancellationToken.None);   // compensation: no orphan content without its row
            throw;
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while (total < limit && (read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - total)), cancellationToken)) > 0)
        {
            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
        }
    }
}

[RequirePermissionPolicy(PermissionCatalog.Files, PermissionAction.Manage)]
public sealed record DeactivatePatientDocumentCommand(Guid DocumentId, string Reason) : ICommand;

public sealed class DeactivatePatientDocumentValidator : AbstractValidator<DeactivatePatientDocumentCommand>
{
    public DeactivatePatientDocumentValidator()
    {
        RuleFor(command => command.DocumentId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(PatientDocument.MaxReasonLength);
    }
}

public sealed class DeactivatePatientDocumentHandler(IPatientDocumentRepository documents, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<DeactivatePatientDocumentCommand>
{
    public async Task Handle(DeactivatePatientDocumentCommand command, CancellationToken cancellationToken)
    {
        var actor = PatientActor.Of(user);
        var document = await documents.GetForUpdateAsync(command.DocumentId, cancellationToken)
            ?? throw new DomainException(PatientCodes.DocumentNotFound, "Document not found.", DomainFault.NotFound);

        document.Deactivate(actor, clock.UtcNow, command.Reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
