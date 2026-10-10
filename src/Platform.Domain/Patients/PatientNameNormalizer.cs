using System.Globalization;
using System.Text;

namespace Platform.Domain.Patients;

/// <summary>
/// Search keys and similarity of patient names in Latin and Arabic script (BR-01, REG-002, intake §13). A key is the
/// name reduced to comparable tokens: diacritics, tatweel and punctuation removed, alef / yaa / taa-marbuta forms
/// unified, lower case, courtesy titles dropped, compound names joined ("عبد الله" = "عبدالله", "abd el rahman" =
/// "abdelrahman"), single spaces. Pure and deterministic: the same function builds the stored keys and the query keys.
/// </summary>
public static class PatientNameNormalizer
{
    /// <summary>Tokens that are titles, not name parts, after normalisation (Latin lower case; Arabic with alef/taa forms unified).</summary>
    private static readonly HashSet<string> Titles = new(StringComparer.Ordinal)
    {
        "mr", "mrs", "miss", "ms", "dr", "master", "baby", "prof", "mister", "madam", "doctor", "professor", "eng", "sir",
        "السيد", "السيده", "الانسه", "انسه", "مدام", "د", "دكتور", "الدكتور", "دكتوره", "الدكتوره", "ا", "استاذ", "الاستاذ",
        "استاذه", "الاستاذه", "م", "مهندس", "المهندس", "مهندسه", "المهندسه", "الطفل", "الطفله", "طفل", "طفله", "رضيع", "الرضيع", "الرضيعه",
    };

    /// <summary>Prefix tokens that belong to the next token ("عبد الله", "ابو بكر", "abdel rahman", "el sayed").</summary>
    private static readonly HashSet<string> JoinWithNext = new(StringComparer.Ordinal)
    {
        "عبد", "ابو", "abd", "abdel", "abdul", "abdal", "abdol", "abu", "abou", "el", "al",
    };

    /// <summary>The normalised key: tokens joined by one space, or an empty string.</summary>
    public static string Normalize(string? name) => string.Join(' ', Tokens(name));

    /// <summary>The comparable tokens of a name, in order.</summary>
    public static IReadOnlyList<string> Tokens(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var raw = Fold(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(token => !Titles.Contains(token)).ToList();
        var tokens = new List<string>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var token = raw[i];
            while (JoinWithNext.Contains(raw[i]) && i + 1 < raw.Count)
            {
                token += raw[++i];
            }

            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// Two names match when their keys are equal, or when they share at least two tokens and every token of the
    /// shorter name occurs in the longer one (order free): "Mohamed Ahmed" matches "Ahmed Mohamed Ali"; "Ahmed" alone
    /// matches only "Ahmed".
    /// </summary>
    public static bool IsSimilar(string? left, string? right) => KeysMatch(Normalize(left), Normalize(right));

    /// <summary><see cref="IsSimilar"/> on keys already normalised (as stored).</summary>
    public static bool KeysMatch(string? leftKey, string? rightKey)
    {
        if (string.IsNullOrEmpty(leftKey) || string.IsNullOrEmpty(rightKey))
        {
            return false;
        }

        if (string.Equals(leftKey, rightKey, StringComparison.Ordinal))
        {
            return true;
        }

        var left = new HashSet<string>(leftKey.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var right = new HashSet<string>(rightKey.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var common = left.Count(right.Contains);
        return common >= 2 && common == Math.Min(left.Count, right.Count);
    }

    private static string Fold(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark || character == '\u0640')
            {
                continue;   // Latin accents, Arabic harakat (incl. the hamza and madda marks FormKD splits off), superscript alef, tatweel
            }

            var mapped = character switch
            {
                '\u0671' or '\u0622' or '\u0623' or '\u0625' => '\u0627',   // alef wasla / madda / hamza above / hamza below → bare alef
                '\u0649' or '\u06CC' or '\u0626' => '\u064A',               // alef maksura, Farsi yeh, yeh with hamza → yeh
                '\u0624' => '\u0648',                                       // waw with hamza → waw
                '\u0629' => '\u0647',                                       // taa marbuta → haa
                '\u06A9' => '\u0643',                                       // keheh → kaf
                >= '\u0660' and <= '\u0669' => (char)('0' + (character - '\u0660')),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (character - '\u06F0')),
                _ => character,
            };

            builder.Append(char.IsLetterOrDigit(mapped) ? char.ToLowerInvariant(mapped) : ' ');
        }

        return builder.ToString();
    }
}
