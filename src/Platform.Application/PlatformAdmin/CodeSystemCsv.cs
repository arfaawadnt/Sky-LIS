using System.Text;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin;

/// <summary>
/// Reads the code file of SA-17 "Import codes": UTF-8 (BOM tolerated), RFC 4180 quoting, a header row naming a code
/// column (<c>code</c>, <c>loinc_num</c>, <c>conceptid</c>, <c>id</c>) and a display column (<c>display</c>,
/// <c>description</c>, <c>long_common_name</c>, <c>term</c>, <c>name</c>), separated by comma, semicolon or tab
/// (detected from the header). At most <see cref="MaxBytes"/> and <see cref="MaxRows"/>; refusals are LIB-010/011.
/// </summary>
public static class CodeSystemCsv
{
    public const long MaxBytes = 20L * 1024 * 1024;
    public const int MaxRows = 500_000;

    private static readonly string[] CodeColumns = ["code", "loinc_num", "conceptid", "concept_id", "id"];
    private static readonly string[] DisplayColumns = ["display", "description", "long_common_name", "term", "name", "label"];

    public static async Task<IReadOnlyList<(string Code, string Display)>> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new StreamReader(content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false), detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
        var buffer = new char[64 * 1024];
        var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > MaxBytes)
            {
                throw new DomainException(PlatformAdminCodes.ImportTooLarge, $"The file exceeds {MaxBytes / (1024 * 1024)} MB.", DomainFault.Validation);
            }
        }

        return Parse(text.ToString());
    }

    public static IReadOnlyList<(string Code, string Display)> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var content = text.TrimStart('﻿');
        var firstLineEnd = content.IndexOfAny(['\r', '\n']);
        var headerLine = firstLineEnd < 0 ? content : content[..firstLineEnd];
        var delimiter = headerLine.Contains('\t', StringComparison.Ordinal) ? '\t' : headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';

        var records = Records(content, delimiter);
        if (records.Count == 0)
        {
            throw Invalid("The file is empty.");
        }

        var header = records[0].Select(name => name.Trim().ToLowerInvariant()).ToList();
        var codeIndex = header.FindIndex(name => CodeColumns.Contains(name, StringComparer.Ordinal));
        var displayIndex = header.FindIndex(name => DisplayColumns.Contains(name, StringComparer.Ordinal));
        if (codeIndex < 0 || displayIndex < 0)
        {
            throw Invalid("The header must name a code column (code) and a display column (display).");
        }

        var rows = new List<(string, string)>(Math.Max(0, records.Count - 1));
        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
            {
                continue;   // blank line
            }

            if (record.Count <= Math.Max(codeIndex, displayIndex))
            {
                throw Invalid($"Line {i + 1} has {record.Count} column(s); the header has {header.Count}.");
            }

            rows.Add((record[codeIndex], record[displayIndex]));
            if (rows.Count > MaxRows)
            {
                throw new DomainException(PlatformAdminCodes.ImportTooLarge, $"The file has more than {MaxRows} rows.", DomainFault.Validation);
            }
        }

        if (rows.Count == 0)
        {
            throw Invalid("The file has a header but no codes.");
        }

        return rows;
    }

    /// <summary>RFC 4180 records: quoted fields may hold delimiters, line breaks and doubled quotes.</summary>
    /// <remarks>Shared with <see cref="LocalityCsv"/> (LIS-CR-008 C-04); an unclosed quote is LIB-010 here.</remarks>
    internal static List<List<string>> Records(string content, char delimiter)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = [];
                line++;
            }
            else
            {
                field.Append(c);
            }
        }

        if (inQuotes)
        {
            throw Invalid($"A quoted field opened before line {line} is not closed.");
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    private static DomainException Invalid(string message) => new(PlatformAdminCodes.ImportFileInvalid, message, DomainFault.Validation);
}
