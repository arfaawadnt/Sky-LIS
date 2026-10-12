using System.Linq.Expressions;
using FluentValidation;

namespace Platform.Application.Abstractions.Persistence;

/// <summary>
/// The order a client asks a paged list for (validation v0.5.0 B-03): a column key of the list's allow-list
/// (<see cref="SortKeys"/>) and a direction. The repository applies it on the server, <b>before</b> paging, so page 2
/// of a sorted list continues page 1. <see cref="None"/> keeps the list's own stable order.
/// </summary>
public readonly record struct ListSort(string? Key, bool Descending)
{
    public const string Ascending = "asc";
    public const string DescendingName = "desc";

    public static ListSort None => default;

    /// <summary>The query-string pair (<c>sort</c>, <c>direction</c>) after validation; a blank key is <see cref="None"/>.</summary>
    public static ListSort Of(string? key, string? direction) =>
        string.IsNullOrWhiteSpace(key) ? None : new ListSort(key.Trim(), string.Equals(direction?.Trim(), DescendingName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The allow-list of the sortable columns of one list, declared beside its query (the validator refuses any other
/// key) and mapped to expressions by the repository through <see cref="SortColumns{T}"/>. Keys are case-insensitive.
/// </summary>
public sealed class SortKeys
{
    private readonly HashSet<string> keys;

    public SortKeys(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        this.keys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        Names = keys.ToArray();
    }

    /// <summary>The keys in declaration order (for messages and tests).</summary>
    public IReadOnlyList<string> Names { get; }

    public int Count => keys.Count;

    public bool Contains(string key) => keys.Contains(key);

    /// <summary>True for a blank key (no sort) or a key of the list.</summary>
    public bool Allows(string? key) => string.IsNullOrWhiteSpace(key) || keys.Contains(key.Trim());
}

/// <summary>
/// Maps the keys of a <see cref="SortKeys"/> allow-list to order expressions (plain LINQ, translated by the provider) and
/// applies the requested one with stable tie-breakers. A text column sorts case-insensitively (R-09). Every allowed key must be mapped and no other key may be: the
/// first <see cref="Apply"/> of an incomplete map throws, so a list's tests catch a key without an expression.
/// </summary>
public sealed class SortColumns<T>
{
    private readonly SortKeys allowed;
    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Func<IOrderedQueryable<T>, IOrderedQueryable<T>>> tieBreakers = [];

    public SortColumns(SortKeys allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        this.allowed = allowed;
    }

    public SortColumns<T> Column<TKey>(string key, Expression<Func<T, TKey>> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(selector);
        if (!allowed.Contains(key))
        {
            throw new InvalidOperationException($"The sort key '{key}' is not in the list's allow-list ({string.Join(", ", allowed.Names)}).");
        }

        if (typeof(TKey) == typeof(string))
        {
            var text = CaseInsensitive((Expression<Func<T, string?>>)(LambdaExpression)selector);
            columns[key] = (query, descending) => descending ? query.OrderByDescending(text) : query.OrderBy(text);
            return this;
        }

        columns[key] = (query, descending) => descending ? query.OrderByDescending(selector) : query.OrderBy(selector);
        return this;
    }

    /// <summary>
    /// A text column orders without regard to case in every list (validation v0.5.0 R-09: "hCG" and "eGFR" sort among
    /// the other names, not after "Sodium" as the byte order of the <c>C</c> collation puts them): the selector becomes
    /// <c>x =&gt; v == null ? null : v.ToLower()</c>, which the provider translates to <c>lower()</c>. The tie-breakers
    /// keep the order of equal names stable.
    /// </summary>
    private static Expression<Func<T, string?>> CaseInsensitive(Expression<Func<T, string?>> selector)
    {
        var toLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var value = selector.Body;
        var lowered = Expression.Condition(
            Expression.Equal(value, Expression.Constant(null, typeof(string))),
            Expression.Constant(null, typeof(string)),
            Expression.Call(value, toLower));
        return Expression.Lambda<Func<T, string?>>(lowered, selector.Parameters);
    }

    /// <summary>An ascending tie-breaker after the chosen column (a unique column last keeps paging stable).</summary>
    public SortColumns<T> ThenBy<TKey>(Expression<Func<T, TKey>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        tieBreakers.Add(ordered => ordered.ThenBy(selector));
        return this;
    }

    /// <summary>The query ordered by <paramref name="sort"/>, or by <paramref name="fallback"/> (the list's own order) when there is none.</summary>
    public IOrderedQueryable<T> Apply(IQueryable<T> query, ListSort sort, Func<IQueryable<T>, IOrderedQueryable<T>> fallback)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(fallback);
        if (columns.Count != allowed.Count)
        {
            var missing = allowed.Names.Where(name => !columns.ContainsKey(name));
            throw new InvalidOperationException($"The sort keys {string.Join(", ", missing)} have no expression.");
        }

        if (sort.Key is null)
        {
            return fallback(query);
        }

        if (!columns.TryGetValue(sort.Key, out var order))
        {
            throw new ArgumentException($"The sort key '{sort.Key}' is not allowed for this list.", nameof(sort));
        }

        var ordered = order(query, sort.Descending);
        foreach (var tieBreaker in tieBreakers)
        {
            ordered = tieBreaker(ordered);
        }

        return ordered;
    }
}

/// <summary>FluentValidation rules of the <c>sort</c> / <c>direction</c> pair of a list query (VALIDATION-400 on anything else).</summary>
public static class ListSortRules
{
    public const int MaxKeyLength = 40;

    public static IRuleBuilderOptions<T, string?> SortKeyOf<T>(this IRuleBuilder<T, string?> rule, SortKeys keys)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(keys);
        return rule.MaximumLength(MaxKeyLength)
            .Must(keys.Allows)
            .WithMessage($"Sort by one of: {string.Join(", ", keys.Names)}.");
    }

    public static IRuleBuilderOptions<T, string?> SortDirection<T>(this IRuleBuilder<T, string?> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Must(direction => string.IsNullOrWhiteSpace(direction)
                                      || string.Equals(direction.Trim(), ListSort.Ascending, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(direction.Trim(), ListSort.DescendingName, StringComparison.OrdinalIgnoreCase))
            .WithMessage("The direction is 'asc' or 'desc'.");
    }
}
