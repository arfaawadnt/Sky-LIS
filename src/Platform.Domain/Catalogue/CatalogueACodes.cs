using System.Globalization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// Refusal codes shared by every aggregate of catalogue half A (intake LIS-INT-CAT-001 §7–§8). Half A owns
/// <c>CAT-001</c>…<c>CAT-199</c>; the aggregate-specific codes live next to each aggregate (<c>&lt;Aggregate&gt;Codes</c>).
/// </summary>
public static class CatalogueACodes
{
    /// <summary>A code is 1…N characters: upper-case letters, digits, '.', '-' or '_', starting with a letter or digit.</summary>
    public const string CodeInvalid = "CAT-001";

    /// <summary>A number is outside its allowed range.</summary>
    public const string NumberOutOfRange = "CAT-008";

    /// <summary>A text is missing or longer than allowed.</summary>
    public const string TextInvalid = "CAT-009";

    /// <summary>A referenced catalogue row does not exist in the tenant or is inactive (BR-24).</summary>
    public const string ReferenceUnavailable = "CAT-040";
}

/// <summary>Shape guards of half A's aggregates and children; every refusal is a coded <see cref="DomainException"/>.</summary>
internal static class CatalogueAGuard
{
    public static string Code(string? value, int maxLength, string what)
    {
        var cleaned = (value ?? string.Empty).Trim().ToUpperInvariant();
        var wellFormed = cleaned.Length >= 1
            && cleaned.Length <= maxLength
            && char.IsAsciiLetterOrDigit(cleaned[0])
            && cleaned.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_');
        if (!wellFormed)
        {
            throw new DomainException(
                CatalogueACodes.CodeInvalid,
                $"The {what} code must be 1 to {maxLength} characters: letters, digits, '.', '-' or '_', starting with a letter or digit.",
                DomainFault.Validation);
        }

        return cleaned;
    }

    public static string Required(string? value, int maxLength, string field)
    {
        var cleaned = (value ?? string.Empty).Trim();
        if (cleaned.Length == 0 || cleaned.Length > maxLength)
        {
            throw new DomainException(CatalogueACodes.TextInvalid, $"{field} is required and holds at most {maxLength} characters.", DomainFault.Validation);
        }

        return cleaned;
    }

    public static string? Optional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        if (cleaned.Length > maxLength)
        {
            throw new DomainException(CatalogueACodes.TextInvalid, $"{field} holds at most {maxLength} characters.", DomainFault.Validation);
        }

        return cleaned;
    }

    public static int Range(int value, int min, int max, string field)
    {
        if (value < min || value > max)
        {
            throw new DomainException(CatalogueACodes.NumberOutOfRange, $"{field} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.", DomainFault.Validation);
        }

        return value;
    }

    public static int? OptionalRange(int? value, int min, int max, string field) =>
        value is { } number ? Range(number, min, max, field) : null;

    /// <summary>A reference code such as LOINC or CPT: letters, digits, '.' and '-' (≤ <paramref name="maxLength"/>).</summary>
    public static string? ReferenceCode(string? value, int maxLength, string field)
    {
        var cleaned = Optional(value, maxLength, field);
        if (cleaned is not null && !cleaned.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
        {
            throw new DomainException(CatalogueACodes.TextInvalid, $"{field} may contain letters, digits, '.' and '-' only.", DomainFault.Validation);
        }

        return cleaned;
    }

    public static void Refuse(string code, string message, DomainFault fault = DomainFault.Validation) =>
        throw new DomainException(code, message, fault);
}
