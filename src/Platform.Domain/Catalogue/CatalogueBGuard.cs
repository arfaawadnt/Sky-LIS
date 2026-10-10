using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// The shared rules of the Catalogue B lists (intake LIS-INT-CTB-001 §8): code shape, optional free texts, phone
/// numbers and id sets. Every method returns the cleaned value or refuses with a coded <see cref="DomainException"/>.
/// </summary>
public static class CatalogueBGuard
{
    public const int MaxCodeLength = 20;
    public const int MaxNameLength = 200;
    public const int MaxLongTextLength = 400;
    public const int MaxFreeTextLength = 100;
    public const int MaxPhoneLength = 20;
    public const int MaxLinks = 200;

    /// <summary>INV-CTB-001: upper-case <c>[A-Z0-9][A-Z0-9._-]*</c> of 1…<paramref name="maxLength"/> characters (the client's <c>CODE_PATTERN</c>).</summary>
    public static string Code(string? code, int maxLength = MaxCodeLength)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        var valid = value.Length >= 1
                    && value.Length <= maxLength
                    && char.IsAsciiLetterOrDigit(value[0])
                    && value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_');
        if (!valid)
        {
            throw new DomainException(
                CatalogueBCodes.CodeInvalid,
                $"The code must be 1 to {maxLength} characters: letters, digits, '.', '-' or '_', starting with a letter or a digit.",
                DomainFault.Validation);
        }

        return value;
    }

    /// <summary>An optional free text: trimmed, empty becomes <see langword="null"/>, longer than <paramref name="maxLength"/> is refused (CAT-201).</summary>
    public static string? OptionalText(string? text, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (value.Length > maxLength)
        {
            throw new DomainException(CatalogueBCodes.TextInvalid, $"The {field} must be at most {maxLength} characters.", DomainFault.Validation);
        }

        return value;
    }

    /// <summary>INV-CTB-012: optional mobile or phone number, <c>+?</c> and 8–15 digits with single spaces between groups.</summary>
    public static string? Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var value = string.Join(' ', phone.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var digits = value.Count(char.IsAsciiDigit);
        var start = value[0] == '+' ? 1 : 0;
        var shapeOk = value.Length <= MaxPhoneLength
                      && value.Length > start
                      && char.IsAsciiDigit(value[start])
                      && value.Skip(start).All(c => char.IsAsciiDigit(c) || c == ' ')
                      && char.IsAsciiDigit(value[^1]);
        if (!shapeOk || digits is < 8 or > 15)
        {
            throw new DomainException(CatalogueBCodes.PhoneInvalid, "The phone number is invalid.", DomainFault.Validation);
        }

        return value;
    }

    /// <summary>INV-CTB-010: a set of ids without empties and duplicates (first occurrence keeps its position), at most <see cref="MaxLinks"/>.</summary>
    public static Guid[] Ids(IEnumerable<Guid>? ids)
    {
        var value = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (value.Length > MaxLinks)
        {
            throw new DomainException(CatalogueBCodes.TooManyLinks, $"At most {MaxLinks} linked records are allowed.", DomainFault.Validation);
        }

        return value;
    }

    /// <summary>True when both sets hold the same ids, in any order.</summary>
    public static bool SameSet(IReadOnlyCollection<Guid> left, IReadOnlyCollection<Guid> right) =>
        left.Count == right.Count && !left.Except(right).Any();
}
