using System.Text;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin;

/// <summary>
/// LIS-CR-008 C-04 (decision D-2): reads the locality file of SA-11 "Import CSV" — UTF-8 (BOM tolerated), RFC 4180 quoting,
/// comma, semicolon or tab (detected from the header). The header names the columns, in any order, case-insensitive:
/// <c>city_en, city_ar, district_en, district_ar, area_en, area_ar</c> (<c>city</c>, <c>district</c>, <c>area</c> and
/// <c>village</c> / <c>village_en</c> / <c>village_ar</c> are accepted too); a city column (English or Arabic) is required,
/// districts and areas are optional. At most <see cref="MaxBytes"/> and <see cref="MaxRows"/> data lines (GEO-024); an unreadable
/// file is GEO-023. Lines are numbered as records of the file, the header being line 1; blank lines are skipped.
/// </summary>
public static class LocalityCsv
{
    public const long MaxBytes = 4L * 1024 * 1024;
    public const int MaxRows = 20_000;

    private static readonly string[] CityEn = ["city_en", "city", "city_name_en", "markaz_en", "markaz"];
    private static readonly string[] CityAr = ["city_ar", "city_name_ar", "markaz_ar"];
    private static readonly string[] DistrictEn = ["district_en", "district", "district_name_en"];
    private static readonly string[] DistrictAr = ["district_ar", "district_name_ar"];
    private static readonly string[] AreaEn = ["area_en", "area", "area_name_en", "village_en", "village"];
    private static readonly string[] AreaAr = ["area_ar", "area_name_ar", "village_ar"];

    public static async Task<IReadOnlyList<LocalityImportRow>> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new StreamReader(content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false), detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
        var buffer = new char[64 * 1024];
        var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > MaxBytes)
            {
                throw TooLarge($"The file exceeds {MaxBytes / (1024 * 1024)} MB.");
            }
        }

        return Parse(text.ToString());
    }

    public static IReadOnlyList<LocalityImportRow> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var content = text.TrimStart('\uFEFF');
        var firstLineEnd = content.IndexOfAny(['\r', '\n']);
        var headerLine = firstLineEnd < 0 ? content : content[..firstLineEnd];
        var delimiter = headerLine.Contains('\t', StringComparison.Ordinal) ? '\t' : headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';

        List<List<string>> records;
        try
        {
            records = CodeSystemCsv.Records(content, delimiter);
        }
        catch (DomainException exception) when (exception.Code == PlatformAdminCodes.ImportFileInvalid)
        {
            throw Invalid(exception.Message);
        }

        if (records.Count == 0 || records[0].All(string.IsNullOrWhiteSpace))
        {
            throw Invalid("The file is empty.");
        }

        var header = records[0].Select(name => name.Trim().ToLowerInvariant().Replace(' ', '_')).ToList();
        int Column(string[] names) => header.FindIndex(name => names.Contains(name, StringComparer.Ordinal));
        var cityEn = Column(CityEn);
        var cityAr = Column(CityAr);
        if (cityEn < 0 && cityAr < 0)
        {
            throw Invalid("The header must name a city column (city_en or city_ar).");
        }

        var districtEn = Column(DistrictEn);
        var districtAr = Column(DistrictAr);
        var areaEn = Column(AreaEn);
        var areaAr = Column(AreaAr);

        var rows = new List<LocalityImportRow>(Math.Max(0, records.Count - 1));
        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;   // a blank line
            }

            string? Cell(int index) => index >= 0 && index < record.Count && !string.IsNullOrWhiteSpace(record[index]) ? record[index].Trim() : null;
            rows.Add(new LocalityImportRow(i + 1, Cell(cityEn), Cell(cityAr), Cell(districtEn), Cell(districtAr), Cell(areaEn), Cell(areaAr)));
            if (rows.Count > MaxRows)
            {
                throw TooLarge($"The file has more than {MaxRows} lines.");
            }
        }

        if (rows.Count == 0)
        {
            throw Invalid("The file has a header but no lines.");
        }

        return rows;
    }

    private static DomainException Invalid(string message) => new(PlatformAdminCodes.LocalityImportFileInvalid, message, DomainFault.Validation);

    private static DomainException TooLarge(string message) => new(PlatformAdminCodes.LocalityImportTooLarge, message, DomainFault.Validation);
}
