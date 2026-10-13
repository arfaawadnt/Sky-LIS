using FluentValidation;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Catalogue;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Catalogue.Commands;

// Report-template images (SRS CP-54 / SA-12, intake LIS-INT-CTB-001 A10): the logo and the signature are files of the
// Files module, uploaded, replaced and removed by a holder of the template's edit key (catalogue.edit).

/// <summary>Where the template images live in the file store: <c>&lt;tenant&gt;/catalogue/&lt;template&gt;/&lt;file id&gt;</c>.</summary>
public static class ReportTemplateImageStorage
{
    public const string Area = "catalogue";

    /// <summary>The key of a template's file in the <b>current</b> tenant (a file id never opens another tenant's content).</summary>
    public static string KeyOf(ICurrentTenant tenant, Guid templateId, Guid fileId)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Template images are read for a resolved tenant only.");
        return FileKeys.Compose(tenantId, Area, templateId, fileId);
    }
}

/// <summary>
/// Uploads the logo or the signature of a report template, or replaces it. <paramref name="Content"/> is the uploaded
/// stream (the controller owns and disposes it); the client's file name and content type are not used: the signature of
/// the bytes decides (PNG, JPEG, WebP; 512 KB at most; CAT-270…272). <paramref name="Image"/> is <c>logo</c> or <c>signature</c>.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UploadReportTemplateImageCommand(Guid ReportTemplateId, string Image, Stream Content) : ICommand<ReportTemplateImageView>;

public sealed class UploadReportTemplateImageValidator : AbstractValidator<UploadReportTemplateImageCommand>
{
    public UploadReportTemplateImageValidator()
    {
        RuleFor(command => command.ReportTemplateId).NotEmpty();
        RuleFor(command => command.Image).EnumName<UploadReportTemplateImageCommand, ReportTemplateImage>();
        RuleFor(command => command.Content).NotNull();
    }
}

public sealed class UploadReportTemplateImageHandler(IReportTemplateRepository templates, IFileStore files, IUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UploadReportTemplateImageCommand, ReportTemplateImageView>
{
    public async Task<ReportTemplateImageView> Handle(UploadReportTemplateImageCommand command, CancellationToken cancellationToken)
    {
        var image = CatalogueBRules.Parse<ReportTemplateImage>(command.Image);
        var template = await templates.GetForUpdateAsync(command.ReportTemplateId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ReportTemplateNotFound, "Report template");

        // Buffer at most 512 KB + 1 byte: the size and the signature are decided before anything reaches the store.
        await using var buffer = new MemoryStream();
        await CopyBoundedAsync(command.Content, buffer, ReportTemplateImageFiles.MaxSizeBytes + 1, cancellationToken);
        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ReportTemplateImageFiles.HeaderLength)).ToArray();
        var contentType = ReportTemplateImageFiles.Inspect(buffer.Length, header);

        buffer.Position = 0;
        var stored = await files.SaveAsync(ReportTemplateImageStorage.Area, template.Id, buffer, ReportTemplateImageFiles.MaxSizeBytes, cancellationToken);
        try
        {
            template.AttachImage(image, stored.FileId, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new ReportTemplateImageView(image.ToString(), stored.FileId, contentType, stored.SizeBytes);
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

/// <summary>Removes the logo or the signature of a report template (CAT-394 when none is attached); the stored file is kept (BR-15).</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record RemoveReportTemplateImageCommand(Guid ReportTemplateId, string Image) : ICommand;

public sealed class RemoveReportTemplateImageValidator : AbstractValidator<RemoveReportTemplateImageCommand>
{
    public RemoveReportTemplateImageValidator()
    {
        RuleFor(command => command.ReportTemplateId).NotEmpty();
        RuleFor(command => command.Image).EnumName<RemoveReportTemplateImageCommand, ReportTemplateImage>();
    }
}

public sealed class RemoveReportTemplateImageHandler(IReportTemplateRepository templates, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<RemoveReportTemplateImageCommand>
{
    public async Task Handle(RemoveReportTemplateImageCommand command, CancellationToken cancellationToken)
    {
        var image = CatalogueBRules.Parse<ReportTemplateImage>(command.Image);
        var template = await templates.GetForUpdateAsync(command.ReportTemplateId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ReportTemplateNotFound, "Report template");
        template.RemoveImage(image, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
