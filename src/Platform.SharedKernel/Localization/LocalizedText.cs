using Platform.SharedKernel.Primitives;

namespace Platform.SharedKernel.Localization;

/// <summary>A bilingual (English / Arabic) text value. Both forms are required; master data carries Arabic names on every record.</summary>
public sealed class LocalizedText : ValueObject
{
    public const int MaxLength = 400;

    public LocalizedText(string en, string ar)
    {
        En = Clean(en, nameof(en));
        Ar = Clean(ar, nameof(ar));
    }

    /// <summary>Reserved for object materialisers (EF Core owned type).</summary>
    private LocalizedText()
    {
        En = string.Empty;
        Ar = string.Empty;
    }

    public string En { get; private set; }

    public string Ar { get; private set; }

    /// <summary>The form for a language tag (<c>ar</c>, <c>ar-EG</c> …); anything else resolves to English.</summary>
    public string For(string? language) =>
        language is not null && language.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? Ar : En;

    public override string ToString() => En;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return En;
        yield return Ar;
    }

    private static string Clean(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException($"Text exceeds {MaxLength} characters.", parameterName);
        }

        return trimmed;
    }
}
