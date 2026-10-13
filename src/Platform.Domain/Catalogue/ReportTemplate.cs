using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>The document a print template lays out (SRS CP-54).</summary>
public enum ReportTemplateType
{
    Results = 0,
    Culture = 1,
    Text = 2,
    Claim = 3,
    Receipt = 4,
    Label = 5,
    JobOrder = 6,
}

/// <summary>The language(s) a template prints in.</summary>
public enum ReportTemplateLanguage
{
    En = 0,
    Ar = 1,
    Both = 2,
}

/// <summary>
/// A print template (SRS CP-54; LDM §12 report templates <c>new_test</c>, <c>new_mega</c>, culture, text; logo,
/// signature image, QR option). The SPA's print kit renders the documents; this record holds the layout choices.
/// Logo and signature are file ids of the Files module (intake A10): they change only through <see cref="AttachImage"/>
/// and <see cref="RemoveImage"/> (the upload use case stores the bytes first), never through the edit of the texts.
/// Header and footer texts are optional and bilingual.
/// </summary>
public sealed class ReportTemplate : CatalogueBRecord
{
    private ReportTemplate()
    {
        Name = null!;
    }

    private ReportTemplate(Guid id)
        : base(id)
    {
        Name = null!;
    }

    /// <summary>English name unique per tenant (INV-CTB-003).</summary>
    public LocalizedText Name { get; private set; }

    public ReportTemplateType Type { get; private set; }

    public Guid? LogoFileId { get; private set; }

    public Guid? SignatureFileId { get; private set; }

    public bool ShowQr { get; private set; }

    public ReportTemplateLanguage Language { get; private set; }

    public LocalizedText? HeaderText { get; private set; }

    public LocalizedText? FooterText { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>): a new template has no images yet.</summary>
    public static ReportTemplate Create(LocalizedText name, ReportTemplateType type, bool showQr, ReportTemplateLanguage language, LocalizedText? headerText, LocalizedText? footerText, DateTimeOffset nowUtc)
    {
        var template = new ReportTemplate(Guid.CreateVersion7());
        template.Update(name, type, showQr, language, headerText, footerText);
        template.Raise(new ReportTemplateCreated(template.Id, template.Type, template.Name.En, nowUtc));
        return template;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): the texts and choices; the images are kept.</summary>
    public void Update(LocalizedText name, ReportTemplateType type, bool showQr, ReportTemplateLanguage language, LocalizedText? headerText, LocalizedText? footerText)
    {
        ArgumentNullException.ThrowIfNull(name);
        Require(Enum.IsDefined(type), CatalogueBCodes.TextInvalid, "The template type is unknown.", DomainFault.Validation);
        Require(Enum.IsDefined(language), CatalogueBCodes.TextInvalid, "The template language is unknown.", DomainFault.Validation);

        Name = name;
        Type = type;
        ShowQr = showQr;
        Language = language;
        HeaderText = headerText;
        FooterText = footerText;
    }

    /// <summary>The file id of one image, or <see langword="null"/> when none is attached.</summary>
    public Guid? FileIdOf(ReportTemplateImage image) => image switch
    {
        ReportTemplateImage.Logo => LogoFileId,
        ReportTemplateImage.Signature => SignatureFileId,
        _ => throw UnknownImage(),
    };

    /// <summary>
    /// T2 Edit (<c>catalogue.edit</c>): attaches or replaces the logo or the signature with a file the Files module has
    /// stored (its content was accepted by <see cref="ReportTemplateImageFiles.Inspect"/>). The previous file stays in the
    /// store (BR-15: no business deletion of stored files).
    /// </summary>
    public void AttachImage(ReportTemplateImage image, Guid fileId, DateTimeOffset nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(fileId, Guid.Empty);
        SetImage(image, fileId);
        Raise(new ReportTemplateImageChanged(Id, image, fileId, nowUtc));
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): clears the logo or the signature (CAT-394 when none is attached).</summary>
    public void RemoveImage(ReportTemplateImage image, DateTimeOffset nowUtc)
    {
        Require(FileIdOf(image) is not null, CatalogueBCodes.TemplateImageNotFound, "The template has no such image.", DomainFault.NotFound);
        SetImage(image, null);
        Raise(new ReportTemplateImageChanged(Id, image, null, nowUtc));
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new ReportTemplateDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new ReportTemplateReactivated(Id, nowUtc);

    private static DomainException UnknownImage() => new(CatalogueBCodes.TextInvalid, "The template image is unknown.", DomainFault.Validation);

    private void SetImage(ReportTemplateImage image, Guid? fileId)
    {
        switch (image)
        {
            case ReportTemplateImage.Logo:
                LogoFileId = fileId;
                break;
            case ReportTemplateImage.Signature:
                SignatureFileId = fileId;
                break;
            default:
                throw UnknownImage();
        }
    }
}
