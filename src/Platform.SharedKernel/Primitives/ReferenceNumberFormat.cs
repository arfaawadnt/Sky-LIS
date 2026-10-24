using System.Globalization;

namespace Platform.SharedKernel.Primitives;

/// <summary>The text forms of reference numbers, shared by the aggregates that own a code format and the counter adapter.</summary>
public static class ReferenceNumberFormat
{
    /// <summary>Widest zero padding a short code accepts.</summary>
    public const int MaxShortWidth = 9;

    /// <summary>
    /// <c>&lt;prefix&gt;-&lt;value&gt;</c>, the value zero-padded to <paramref name="width"/> digits; a longer value keeps all its
    /// digits (<c>DR-1000</c> after <c>DR-999</c>).
    /// </summary>
    public static string Short(string prefix, long value, int width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxShortWidth);
        var digits = value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        return string.Concat(prefix, "-", digits);
    }
}
