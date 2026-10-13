using FluentValidation;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue.Commands;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Queries;

/// <summary>The bytes of a report-template image and the content type detected from them (the controller streams and disposes them).</summary>
public sealed record ReportTemplateImageContent(Stream Content, string ContentType);

/// <summary>
/// The logo or the signature of a report template, for every signed-in member of the tenant: the template editor shows
/// it, and every printed result report that uses the template carries it (the results users hold no catalogue key).
/// Own tenant only: the template is read through the tenant filter and RLS, and the file key is composed with the
/// session's tenant. CAT-388 when the template is unknown, CAT-394 when the image is not attached or its content is gone.
/// </summary>
[RequireAuthenticatedActor]
public sealed record GetReportTemplateImageQuery(Guid ReportTemplateId, string Image) : IQuery<ReportTemplateImageContent>;

public sealed class GetReportTemplateImageValidator : AbstractValidator<GetReportTemplateImageQuery>
{
    public GetReportTemplateImageValidator()
    {
        RuleFor(query => query.ReportTemplateId).NotEmpty();
        RuleFor(query => query.Image).EnumName<GetReportTemplateImageQuery, ReportTemplateImage>();
    }
}

public sealed class GetReportTemplateImageHandler(IReportTemplateRepository templates, IFileStore files, ICurrentTenant tenant)
    : IQueryHandler<GetReportTemplateImageQuery, ReportTemplateImageContent>
{
    public async Task<ReportTemplateImageContent> Handle(GetReportTemplateImageQuery query, CancellationToken cancellationToken)
    {
        var image = CatalogueBRules.Parse<ReportTemplateImage>(query.Image);
        var template = await templates.GetListItemAsync(query.ReportTemplateId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ReportTemplateNotFound, "Report template");
        var fileId = (image == ReportTemplateImage.Logo ? template.LogoFileId : template.SignatureFileId) ?? throw NotFound();

        var content = await files.OpenReadAsync(ReportTemplateImageStorage.KeyOf(tenant, template.Id, fileId), cancellationToken) ?? throw NotFound();
        try
        {
            // the type is decided by the bytes, as on upload (the store keeps bytes only)
            var header = new byte[ReportTemplateImageFiles.HeaderLength];
            var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
            var contentType = ReportTemplateImageFiles.DetectContentType(header.AsSpan(0, read)) ?? throw NotFound();
            content.Position = 0;
            return new ReportTemplateImageContent(content, contentType);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    private static DomainException NotFound() => new(CatalogueBCodes.TemplateImageNotFound, "The template has no such image.", DomainFault.NotFound);
}
