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
/// Logo and signature are file ids of the Files module. Header and footer texts are optional and bilingual.
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

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static ReportTemplate Create(LocalizedText name, ReportTemplateType type, Guid? logoFileId, Guid? signatureFileId, bool showQr, ReportTemplateLanguage language, LocalizedText? headerText, LocalizedText? footerText, DateTimeOffset nowUtc)
    {
        var template = new ReportTemplate(Guid.CreateVersion7());
        template.Update(name, type, logoFileId, signatureFileId, showQr, language, headerText, footerText);
        template.Raise(new ReportTemplateCreated(template.Id, template.Type, template.Name.En, nowUtc));
        return template;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Update(LocalizedText name, ReportTemplateType type, Guid? logoFileId, Guid? signatureFileId, bool showQr, ReportTemplateLanguage language, LocalizedText? headerText, LocalizedText? footerText)
    {
        ArgumentNullException.ThrowIfNull(name);
        Require(Enum.IsDefined(type), CatalogueBCodes.TextInvalid, "The template type is unknown.", DomainFault.Validation);
        Require(Enum.IsDefined(language), CatalogueBCodes.TextInvalid, "The template language is unknown.", DomainFault.Validation);

        Name = name;
        Type = type;
        LogoFileId = logoFileId == Guid.Empty ? null : logoFileId;
        SignatureFileId = signatureFileId == Guid.Empty ? null : signatureFileId;
        ShowQr = showQr;
        Language = language;
        HeaderText = headerText;
        FooterText = footerText;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new ReportTemplateDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new ReportTemplateReactivated(Id, nowUtc);
}
