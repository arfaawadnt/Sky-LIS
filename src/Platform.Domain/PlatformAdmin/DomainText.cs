using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>Text guards shared by the platform aggregates: trimmed, length-bounded, coded refusals (never an ArgumentException).</summary>
internal static class DomainText
{
    public static string Required(string? value, int maxLength, string code, string field)
    {
        var cleaned = (value ?? string.Empty).Trim();
        if (cleaned.Length == 0 || cleaned.Length > maxLength)
        {
            throw new DomainException(code, $"{field} is required and must not exceed {maxLength} characters.", DomainFault.Validation);
        }

        return cleaned;
    }

    public static string? Optional(string? value, int maxLength, string code, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        if (cleaned.Length > maxLength)
        {
            throw new DomainException(code, $"{field} must not exceed {maxLength} characters.", DomainFault.Validation);
        }

        return cleaned;
    }

    public static LocalizedText Localized(string? en, string? ar, int maxLength, string code, string field) =>
        new(Required(en, maxLength, code, field + " (English)"), Required(ar, maxLength, code, field + " (Arabic)"));

    /// <summary>A minimal e-mail shape check (one '@', a dot in the domain, no spaces); the address is verified by use, not by syntax.</summary>
    public static string? Email(string? value, string code)
    {
        var cleaned = Optional(value, 254, code, "The e-mail address");
        if (cleaned is null)
        {
            return null;
        }

        var at = cleaned.IndexOf('@', StringComparison.Ordinal);
        var valid = at > 0
                    && at == cleaned.LastIndexOf('@')
                    && cleaned.IndexOf('.', at) > at + 1
                    && !cleaned.EndsWith('.')
                    && !cleaned.Any(char.IsWhiteSpace);
        if (!valid)
        {
            throw new DomainException(code, "The e-mail address is not valid.", DomainFault.Validation);
        }

        return cleaned.ToLowerInvariant();
    }

    public static decimal Money(decimal amount, string code, string field, bool allowZero)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded < 0 || (!allowZero && rounded == 0) || rounded > 100_000_000m)
        {
            throw new DomainException(code, $"{field} must be {(allowZero ? "zero or more" : "greater than zero")} and at most 100,000,000.", DomainFault.Validation);
        }

        return rounded;
    }
}
