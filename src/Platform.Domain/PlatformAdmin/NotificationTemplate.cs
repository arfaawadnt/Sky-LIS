using System.Text.RegularExpressions;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// An editable SMS or e-mail text (SA-20) in English and Arabic with <c>{placeholder}</c> tokens. Platform reference
/// data (<c>saas.notification_templates</c>); the code is unique (INV-PLT-040) and is what senders ask for
/// (<c>otp</c>, <c>password-recovery</c>, <c>portal-url</c>, <c>panic-value</c> …). Only the
/// placeholders of <see cref="Placeholders"/> are accepted (NTF-003). An inactive template is not used: the sender
/// falls back to the platform's built-in text for the code.
/// </summary>
public sealed partial class NotificationTemplate : AggregateRoot
{
    public const int MaxCodeLength = 40;
    public const int MaxTextLength = 2000;
    public const int MaxSubjectLength = 200;

    private NotificationTemplate()
    {
        Code = string.Empty;
        TextEn = string.Empty;
        TextAr = string.Empty;
    }

    private NotificationTemplate(Guid id, string code, TemplateChannel channel, TemplateText text)
        : base(id)
    {
        Code = code;
        Channel = channel;
        TextEn = text.TextEn;
        TextAr = text.TextAr;
        SubjectEn = text.SubjectEn;
        SubjectAr = text.SubjectAr;
        IsActive = true;
    }

    public string Code { get; private set; }

    public TemplateChannel Channel { get; private set; }

    public string? SubjectEn { get; private set; }

    public string? SubjectAr { get; private set; }

    public string TextEn { get; private set; }

    public string TextAr { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static NotificationTemplate Create(string code, TemplateChannel channel, string textEn, string textAr, string? subjectEn, string? subjectAr) =>
        new(Guid.CreateVersion7(), CleanCode(code), channel, Validate(channel, textEn, textAr, subjectEn, subjectAr));

    /// <summary>Edits the channel and the texts; the code is the natural key and never changes.</summary>
    public void Update(TemplateChannel channel, string textEn, string textAr, string? subjectEn, string? subjectAr)
    {
        var text = Validate(channel, textEn, textAr, subjectEn, subjectAr);
        Channel = channel;
        TextEn = text.TextEn;
        TextAr = text.TextAr;
        SubjectEn = text.SubjectEn;
        SubjectAr = text.SubjectAr;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>Lower-case letters, digits and '-', 2–40 characters (NTF-001).</summary>
    public static string CleanCode(string? code)
    {
        var value = (code ?? string.Empty).Trim().ToLowerInvariant();
        Require(
            value.Length is >= 2 and <= MaxCodeLength && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') && value[0] != '-' && value[^1] != '-',
            PlatformAdminCodes.TemplateCodeInvalid,
            $"The template code is 2 to {MaxCodeLength} characters of a-z, 0-9 and '-'.",
            DomainFault.Validation);
        return value;
    }

    /// <summary>The <c>{name}</c> tokens of a text, in order of appearance (duplicates kept once).</summary>
    public static IReadOnlyList<string> TokensOf(string text) =>
        TokenPattern().Matches(text ?? string.Empty).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    private static TemplateText Validate(TemplateChannel channel, string textEn, string textAr, string? subjectEn, string? subjectAr)
    {
        Require(Enum.IsDefined(channel), PlatformAdminCodes.TemplateTextInvalid, "The channel is Sms or Email.", DomainFault.Validation);
        var en = CheckText(textEn, "The English text");
        var ar = CheckText(textAr, "The Arabic text");
        var subjectEnglish = DomainText.Optional(subjectEn, MaxSubjectLength, PlatformAdminCodes.SubjectRequired, "The English subject");
        var subjectArabic = DomainText.Optional(subjectAr, MaxSubjectLength, PlatformAdminCodes.SubjectRequired, "The Arabic subject");
        if (channel == TemplateChannel.Email)
        {
            Require(subjectEnglish is not null && subjectArabic is not null, PlatformAdminCodes.SubjectRequired, "An e-mail template needs a subject in both languages.", DomainFault.Validation);
        }

        return new TemplateText(en, ar, subjectEnglish, subjectArabic);
    }

    private static string CheckText(string text, string field)
    {
        var cleaned = DomainText.Required(text, MaxTextLength, PlatformAdminCodes.TemplateTextInvalid, field);
        var withoutTokens = TokenPattern().Replace(cleaned, string.Empty);
        Require(!withoutTokens.Contains('{', StringComparison.Ordinal) && !withoutTokens.Contains('}', StringComparison.Ordinal),
            PlatformAdminCodes.TemplateTextInvalid, $"{field} has an unbalanced '{{' or '}}': placeholders are written {{name}}.", DomainFault.Validation);

        var unknown = TokensOf(cleaned).Where(token => !Placeholders.IsKnown(token)).ToList();
        Require(unknown.Count == 0, PlatformAdminCodes.UnknownPlaceholder, $"{field} uses unknown placeholders: {string.Join(", ", unknown.Select(token => "{" + token + "}"))}.", DomainFault.Validation);
        return cleaned;
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TokenPattern();

    private sealed record TemplateText(string TextEn, string TextAr, string? SubjectEn, string? SubjectAr);

    /// <summary>
    /// The placeholders senders fill (Application <c>NotificationTemplateCodes</c>) and the short SRS spellings of
    /// SA-20 (<c>{lab}</c>, <c>{acc}</c>), which are aliases rewritten to the canonical names when a text is served.
    /// </summary>
    public static class Placeholders
    {
        public static readonly IReadOnlyList<string> Canonical =
            ["code", "minutes", "url", "username", "labName", "patient", "accession", "test", "value", "number", "requester", "treasury"];

        public static readonly IReadOnlyDictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.Ordinal) { ["lab"] = "labName", ["acc"] = "accession" };

        public static bool IsKnown(string token) => Canonical.Contains(token, StringComparer.Ordinal) || Aliases.ContainsKey(token);

        /// <summary>Rewrites alias tokens (<c>{lab}</c> → <c>{labName}</c>) so the sender's placeholder names apply.</summary>
        public static string ToCanonical(string text)
        {
            var result = text ?? string.Empty;
            foreach (var (alias, canonical) in Aliases)
            {
                result = result.Replace("{" + alias + "}", "{" + canonical + "}", StringComparison.Ordinal);
            }

            return result;
        }
    }
}
