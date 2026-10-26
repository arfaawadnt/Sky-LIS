using System.Text;
using System.Text.Json;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Tenancy;

/// <summary>
/// LIS-CR-014 T2: the member's list views as one small JSON document stored on the membership
/// (<c>app.tenant_memberships.list_preferences</c>): <c>{ "&lt;listKey&gt;": { "hidden": ["col", …] } }</c>. A list key names a
/// table of the SPA (<c>visits</c>, <c>tracker</c>), a column key the table's column; both are short identifiers
/// (<see cref="IsKey"/>). The document is written by the domain only (ordered keys, compact), so the stored text is
/// stable; it is at most <see cref="MaxLength"/> characters (TEN-015 beyond, or for a key that is not an identifier).
/// Reading is tolerant: a value that is not such a document reads as "no preference" and the next save replaces it.
/// </summary>
public static class ListPreferenceDocument
{
    public const int MaxLength = 4096;
    public const int MaxKeyLength = 64;
    public const int MaxHiddenColumns = 40;
    private const string HiddenProperty = "hidden";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Empty = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>A list or column key: 1–64 characters, letters, digits, <c>.</c>, <c>_</c>, <c>-</c>, starting with a letter or digit.</summary>
    public static bool IsKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || !char.IsAsciiLetterOrDigit(key[0]))
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The hidden columns per list of a stored document; empty for null or a value that is not such a document.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var lists = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var list in document.RootElement.EnumerateObject())
            {
                if (!IsKey(list.Name) || list.Value.ValueKind != JsonValueKind.Object || !list.Value.TryGetProperty(HiddenProperty, out var hidden) || hidden.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var columns = hidden.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(IsKey)
                    .Select(item => item!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (columns.Length > 0)
                {
                    lists[list.Name] = columns;
                }
            }

            return lists;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// The document with the hidden columns of one list replaced (an empty set removes the list; no list left = null).
    /// Refuses with TEN-015 a key that is not an identifier, more than <see cref="MaxHiddenColumns"/> columns, or a
    /// document longer than <see cref="MaxLength"/>.
    /// </summary>
    public static string? With(string? json, string listKey, IEnumerable<string> hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        var key = listKey?.Trim();
        Refuse(!IsKey(key), $"The list key must be 1 to {MaxKeyLength} letters, digits, '.', '_' or '-'.");

        var columns = hidden.Select(column => column?.Trim()).ToList();
        Refuse(columns.Any(column => !IsKey(column)), $"Every column key must be 1 to {MaxKeyLength} letters, digits, '.', '_' or '-'.");
        var distinct = columns.Select(column => column!).Distinct(StringComparer.Ordinal).ToArray();
        Refuse(distinct.Length > MaxHiddenColumns, $"At most {MaxHiddenColumns} hidden columns per list.");

        var lists = new SortedDictionary<string, IReadOnlyList<string>>(Parse(json).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        if (distinct.Length == 0)
        {
            lists.Remove(key!);
        }
        else
        {
            lists[key!] = distinct;
        }

        if (lists.Count == 0)
        {
            return null;
        }

        var text = Write(lists);
        Refuse(text.Length > MaxLength, $"The list preferences are limited to {MaxLength} characters.");
        return text;
    }

    private static string Write(SortedDictionary<string, IReadOnlyList<string>> lists)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (list, columns) in lists)
            {
                writer.WriteStartObject(list);
                writer.WriteStartArray(HiddenProperty);
                foreach (var column in columns)
                {
                    writer.WriteStringValue(column);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Refuse(bool condition, string message)
    {
        if (condition)
        {
            throw new DomainException(TenancyCodes.ListPreferencesInvalid, message, DomainFault.Validation);
        }
    }
}
