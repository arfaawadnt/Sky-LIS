using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A reusable report comment of the comment library (SRS CP-47; LDM §5.4 "Insert Comment" from the unit's template
/// library). The chemist types <c>%</c> and the short name to insert the text. A comment without a clinical group is
/// offered in every group.
/// </summary>
public sealed class CommentTemplate : CatalogueBRecord
{
    public const int MaxShortNameLength = 32;

    private CommentTemplate()
    {
        ShortName = string.Empty;
        Text = null!;
    }

    private CommentTemplate(Guid id, string shortName)
        : base(id)
    {
        ShortName = shortName;
        Text = null!;
    }

    /// <summary>Upper-case token typed after <c>%</c>; unique per tenant, immutable.</summary>
    public string ShortName { get; private set; }

    public LocalizedText Text { get; private set; }

    /// <summary>The clinical group (Catalogue A) the comment belongs to; <see langword="null"/> = all groups.</summary>
    public Guid? ClinicalGroupId { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static CommentTemplate Create(string shortName, LocalizedText text, Guid? clinicalGroupId, DateTimeOffset nowUtc)
    {
        var template = new CommentTemplate(Guid.CreateVersion7(), CatalogueBGuard.Code(shortName, MaxShortNameLength));
        template.Update(text, clinicalGroupId);
        template.Raise(new CommentTemplateCreated(template.Id, template.ShortName, template.Text.En, nowUtc));
        return template;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Update(LocalizedText text, Guid? clinicalGroupId)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        ClinicalGroupId = clinicalGroupId == Guid.Empty ? null : clinicalGroupId;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new CommentTemplateDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new CommentTemplateReactivated(Id, nowUtc);
}
