using System.Globalization;
using System.Text;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;

namespace Platform.Application.Catalogue;

/// <summary>The language of a Catalogue B export file: its headers, flags and labels (UAT B-09).</summary>
public enum CsvLanguage
{
    En = 0,
    Ar = 1,
}

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

    /// <summary>The language of the file: the page passes the user's language (<c>lang=ar</c>), English otherwise (UAT B-09).</summary>
    public static CsvLanguage LanguageOf(string? lang) => string.Equals(lang?.Trim(), "ar", StringComparison.OrdinalIgnoreCase) ? CsvLanguage.Ar : CsvLanguage.En;

    /// <summary>The file; the English headers are translated into the file's language.</summary>
    public static CatalogueBExportFile Build<T>(string name, IReadOnlyList<string> headers, IEnumerable<T> rows, Func<T, IEnumerable<string?>> cells, DateTimeOffset nowUtc, CsvLanguage language = CsvLanguage.En)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cells);

        var text = new StringBuilder();
        AppendLine(text, headers.Select(header => Text(language, header)));
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

    /// <summary>A flag as the grid says it: Yes / No in the file's language (never true / false).</summary>
    public static string Flag(bool value, CsvLanguage language = CsvLanguage.En) => Text(language, value ? "Yes" : "No");

    /// <summary>An enum value as the grid labels it (<c>answerType</c>, <c>OptionList</c> → "Option list"), in the file's language; unknown values pass through.</summary>
    public static string Label(CsvLanguage language, string kind, string? value) =>
        Labels.TryGetValue($"{kind}.{value}", out var label) ? Text(language, label) : value ?? string.Empty;

    /// <summary>A fixed text of the file (header, "All", label) in the file's language; English when no translation is known.</summary>
    public static string Text(CsvLanguage language, string english) =>
        language == CsvLanguage.Ar && Arabic.TryGetValue(english, out var arabic) ? arabic : english;

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Linked services as <c>CODE | CODE</c>.</summary>
    public static string Services(IEnumerable<LinkedServiceView> services) => string.Join(" | ", services.Select(service => service.Code));

    // The labels and translations are the ones the Catalogue B pages show (public/i18n/catalogue-b.*.json), so the file reads like the grid.
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["answerType.OptionList"] = "Option list",
        ["answerType.YesNo"] = "Yes / No",
        ["answerType.Text"] = "Text",
        ["templateType.Text"] = "Text report",
        ["gender.Any"] = "All patients",
        ["gender.Male"] = "Male",
        ["gender.Female"] = "Female",
        ["reasonKind.Cancellation"] = "Cancellation",
        ["reasonKind.Discount"] = "Discount",
        ["reasonKind.Refund"] = "Refund",
        ["reasonKind.Rejection"] = "Sample rejection",
        ["reasonKind.Transfer"] = "Money transfer",
        ["templateType.Claim"] = "Claim",
        ["templateType.Culture"] = "Culture",
        ["templateType.JobOrder"] = "Job order",
        ["templateType.Label"] = "Label",
        ["templateType.Receipt"] = "Receipt",
        ["templateType.Results"] = "Results",
        ["templateLanguage.En"] = "English",
        ["templateLanguage.Ar"] = "Arabic",
        ["templateLanguage.Both"] = "English and Arabic",
    };

    private static readonly Dictionary<string, string> Arabic = new(StringComparer.Ordinal)
    {
        ["Yes"] = "نعم",
        ["No"] = "لا",
        ["All"] = "الكل",
        ["All branches"] = "كل الفروع",
        ["All services"] = "كل الخدمات",
        ["All groups"] = "كل المجموعات",
        ["Option list"] = "قائمة خيارات",
        ["Yes / No"] = "نعم / لا",
        ["Text"] = "نص",
        ["Text report"] = "تقرير نصي",
        ["All patients"] = "كل المرضى",
        ["Male"] = "ذكر",
        ["Female"] = "أنثى",
        ["Cancellation"] = "إلغاء",
        ["Discount"] = "خصم",
        ["Refund"] = "استرداد",
        ["Sample rejection"] = "رفض العينة",
        ["Money transfer"] = "تحويل مالي",
        ["Claim"] = "المطالبة",
        ["Culture"] = "المزرعة",
        ["Job order"] = "أمر الشغل",
        ["Label"] = "الملصق",
        ["Receipt"] = "الإيصال",
        ["Results"] = "النتائج",
        ["English"] = "الإنجليزية",
        ["Arabic"] = "العربية",
        ["English and Arabic"] = "الإنجليزية والعربية",
        ["Code"] = "الرمز",
        ["Name (EN)"] = "الاسم (إنجليزي)",
        ["Name (AR)"] = "الاسم (عربي)",
        ["Question (EN)"] = "السؤال (إنجليزي)",
        ["Question (AR)"] = "السؤال (عربي)",
        ["Answer type"] = "نوع الإجابة",
        ["Options"] = "الخيارات",
        ["Gender"] = "الجنس",
        ["Services"] = "الخدمات",
        ["Active"] = "نشط",
        ["Short name"] = "الاسم المختصر",
        ["Text (EN)"] = "النص (إنجليزي)",
        ["Text (AR)"] = "النص (عربي)",
        ["Clinical group (EN)"] = "المجموعة السريرية (إنجليزي)",
        ["Clinical group (AR)"] = "المجموعة السريرية (عربي)",
        ["Specialty"] = "التخصص",
        ["Occupation"] = "المهنة",
        ["Branches"] = "الفروع",
        ["Share %"] = "نسبة المشاركة %",
        ["Phone"] = "الهاتف",
        ["ICD code"] = "رمز ICD",
        ["Payer id"] = "معرّف الجهة",
        ["Contract tier id"] = "معرّف فئة التعاقد",
        ["Courier (EN)"] = "المندوب (إنجليزي)",
        ["Courier (AR)"] = "المندوب (عربي)",
        ["Doctor (EN)"] = "الطبيب (إنجليزي)",
        ["Doctor (AR)"] = "الطبيب (عربي)",
        ["Triggers"] = "الخدمات المُطلِقة",
        ["Added services"] = "الخدمات المضافة",
        ["Kind"] = "النوع",
        ["Type"] = "النوع",
        ["Logo"] = "الشعار",
        ["Signature"] = "التوقيع",
        ["QR"] = "رمز QR",
        ["Language"] = "اللغة",
        ["Header (EN)"] = "الترويسة (إنجليزي)",
        ["Header (AR)"] = "الترويسة (عربي)",
        ["Footer (EN)"] = "التذييل (إنجليزي)",
        ["Footer (AR)"] = "التذييل (عربي)",
        ["Drug type (EN)"] = "نوع الدواء (إنجليزي)",
        ["Drug type (AR)"] = "نوع الدواء (عربي)",
        ["Abbreviation"] = "الاختصار",
        ["Commercial name (EN)"] = "الاسم التجاري (إنجليزي)",
        ["Commercial name (AR)"] = "الاسم التجاري (عربي)",
        ["Order"] = "الترتيب",
    };

    private static void AppendLine(StringBuilder text, IEnumerable<string?> values)
    {
        text.Append(string.Join(',', values.Select(Cell)));
        text.Append("\r\n");
    }
}
