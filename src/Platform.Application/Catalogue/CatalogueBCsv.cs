using System.Globalization;
using System.Text;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.Application.Catalogue;

/// <summary>
/// CSV export of the Catalogue B registers (intake A12): RFC 4180 quoting, UTF-8 with a byte-order mark so spreadsheet
/// programs read Arabic, and formula-injection protection (a cell starting with <c>= + - @</c>, tab or carriage return
/// is prefixed with an apostrophe). At most <see cref="MaxRows"/> rows.
/// </summary>
public static class CatalogueBCsv
{
    public const int MaxRows = 10_000;
    public const string ContentType = "text/csv";

    /// <summary>Every row of a paged register, page after page, up to <see cref="MaxRows"/>.</summary>
    public static async Task<IReadOnlyList<T>> AllAsync<T>(Func<PageRequest, Task<Paged<T>>> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var rows = new List<T>();
        for (var number = 1; rows.Count < MaxRows; number++)
        {
            var current = await page(PageRequest.Normalized(number, PageRequest.MaxPageSize)).ConfigureAwait(false);
            rows.AddRange(current.Items.Take(MaxRows - rows.Count));
            if (current.Items.Count < current.PageSize || number >= current.TotalPages)
            {
                break;
            }
        }

        return rows;
    }

    public static CatalogueBExportFile Build<T>(string name, IReadOnlyList<string> headers, IEnumerable<T> rows, Func<T, IEnumerable<string?>> cells, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cells);

        var text = new StringBuilder();
        AppendLine(text, headers);
        foreach (var row in rows)
        {
            AppendLine(text, cells(row));
        }

        var fileName = string.Create(CultureInfo.InvariantCulture, $"{name}-{nowUtc:yyyyMMdd-HHmm}.csv");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
        return new CatalogueBExportFile(fileName, ContentType, bytes);
    }

    /// <summary>One cell: neutralised when it could start a formula, quoted when it holds a separator, a quote or a line break.</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var safe = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }

    public static string Flag(bool value) => value ? "Yes" : "No";

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Linked services as <c>CODE | CODE</c>.</summary>
    public static string Services(IEnumerable<LinkedServiceView> services) => string.Join(" | ", services.Select(service => service.Code));

    private static void AppendLine(StringBuilder text, IEnumerable<string?> values)
    {
        text.Append(string.Join(',', values.Select(Cell)));
        text.Append("\r\n");
    }
}
