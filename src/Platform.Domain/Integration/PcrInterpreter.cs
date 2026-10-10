using System.Globalization;
using System.Text;

namespace Platform.Domain.Integration;

/// <summary>One gene of one sample as read from the run file and called against the kit's thresholds.</summary>
public sealed record PcrGeneReading(string Gene, decimal? Ct, PcrGeneCall Call, bool IsInternalControl);

/// <summary>A sample of the run: its readings and verdict, or the <see cref="Problem"/> that prevented one (a missing gene, a duplicate reading).</summary>
public sealed record PcrSampleVerdict(string SampleNumber, string? Well, IReadOnlyList<PcrGeneReading> Genes, PcrVerdict? Verdict, string? VerdictText, string? Problem)
{
    /// <summary>The audit trail of the reading, one entry per gene: <c>&lt;well&gt;-&lt;gene&gt;-&lt;Ct&gt;</c> (LDM manual §10 log "Flag").</summary>
    public IReadOnlyList<string> Trail => Genes.Select(reading => $"{Well ?? "?"}-{reading.Gene}-{(reading.Ct is { } ct ? ct.ToString(CultureInfo.InvariantCulture) : "NA")}").ToList();
}

/// <summary>The result of reading a run file: readable (move it to backup) or refused with a code and reason (move it to failed).</summary>
public sealed record PcrRunInterpretation(bool Readable, string? RefusalCode, string? RefusalReason, string? FileSerial, IReadOnlyList<PcrSampleVerdict> Samples)
{
    public RunFileDisposition Disposition => Readable ? RunFileDisposition.Backup : RunFileDisposition.Failed;

    public static PcrRunInterpretation Refused(string code, string reason, string? serial = null) => new(false, code, reason, serial, []);
}

/// <summary>
/// Reads a PCR run file with a kit (SRS CP-62; LDM §10, manual §14.3–§14.9). Pure: no I/O, no clock. Steps:
/// (1) locate the header row (the first row naming the sample, gene and value columns); (2) read the instrument serial
/// from the rows above it (a cell equal to the kit's serial label followed by its value, or <c>label: value</c>) and refuse
/// the file when it differs from the kit's lock or from the serial the agent declared (INV-INT-009, INT-037); (3) read the
/// patient rows (controls skipped through the content column), refusing the file on a gene the kit does not know or on a
/// value that is neither a number nor a no-amplification token (INT-038 — LDM's "renamed gene" and "changed layout"
/// failures); (4) call every gene and combine the calls into the verdict by the kit's rules.
/// </summary>
public static class PcrInterpreter
{
    public const int MaxContentLength = 2 * 1024 * 1024;
    public const int MaxRows = 20_000;

    public static PcrRunInterpretation Interpret(PcrKit kit, string? declaredSerial, string content)
    {
        ArgumentNullException.ThrowIfNull(kit);
        if (string.IsNullOrWhiteSpace(content))
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, "The run file is empty.");
        }

        if (content.Length > MaxContentLength)
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, "The run file exceeds 2 MB.");
        }

        var layout = kit.Layout;
        var rows = content.Split('\n').Select(line => Split(line.TrimEnd('\r'), layout.DelimiterChar)).ToList();
        if (rows.Count > MaxRows)
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, $"The run file has more than {MaxRows} rows.");
        }

        var headerIndex = rows.FindIndex(row => IndexOf(row, layout.SampleColumn) >= 0 && IndexOf(row, layout.GeneColumn) >= 0 && IndexOf(row, kit.ValueColumn) >= 0);
        if (headerIndex < 0)
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, $"No header row names the columns '{layout.SampleColumn}', '{layout.GeneColumn}' and '{kit.ValueColumn}'.");
        }

        var fileSerial = ReadSerial(rows.Take(headerIndex), layout.SerialLabel);
        var declared = string.IsNullOrWhiteSpace(declaredSerial) ? null : declaredSerial.Trim();
        if (fileSerial is not null && declared is not null && !string.Equals(fileSerial, declared, StringComparison.OrdinalIgnoreCase))
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileFromOtherInstrument, $"The file names instrument '{fileSerial}' but was sent as '{declared}'.", fileSerial);
        }

        var serial = fileSerial ?? declared;
        if (kit.SerialLock is { } expected && !string.Equals(serial, expected, StringComparison.OrdinalIgnoreCase))
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileFromOtherInstrument, serial is null ? "The file carries no instrument serial number." : $"The file comes from instrument '{serial}', not '{expected}'.", serial);
        }

        var header = rows[headerIndex];
        var sampleAt = IndexOf(header, layout.SampleColumn);
        var geneAt = IndexOf(header, layout.GeneColumn);
        var valueAt = IndexOf(header, kit.ValueColumn);
        var wellAt = layout.WellColumn is null ? -1 : IndexOf(header, layout.WellColumn);
        var contentAt = layout.ContentColumn is null ? -1 : IndexOf(header, layout.ContentColumn);
        if (layout.ContentColumn is not null && contentAt < 0)
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, $"The header has no '{layout.ContentColumn}' column.", serial);
        }

        var genes = kit.Genes.ToDictionary(gene => gene.Name, StringComparer.OrdinalIgnoreCase);
        var samples = new List<SampleReadings>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var sampleNumber = Cell(row, sampleAt);
            if (sampleNumber.Length == 0 || (contentAt >= 0 && !string.Equals(Cell(row, contentAt), layout.PatientContentValue, StringComparison.OrdinalIgnoreCase)))
            {
                continue;   // a control well, or an unnamed one
            }

            var geneName = Cell(row, geneAt);
            if (!genes.TryGetValue(geneName, out var gene))
            {
                return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, $"The gene '{geneName}' of sample '{sampleNumber}' is not in the kit.", serial);
            }

            var raw = Cell(row, valueAt);
            decimal? ct = null;
            if (raw.Length > 0 && !layout.NoAmplificationTokens.Contains(raw, StringComparer.OrdinalIgnoreCase))
            {
                if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var parsed))
                {
                    return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, $"'{raw}' for sample '{sampleNumber}' is neither a Ct value nor a no-amplification token.", serial);
                }

                ct = parsed;
            }

            var readings = samples.Find(sample => string.Equals(sample.SampleNumber, sampleNumber, StringComparison.OrdinalIgnoreCase));
            if (readings is null)
            {
                readings = new SampleReadings(sampleNumber, wellAt >= 0 ? NullIfEmpty(Cell(row, wellAt)) : null);
                samples.Add(readings);
            }

            readings.Add(gene, ct);
        }

        if (samples.Count == 0)
        {
            return PcrRunInterpretation.Refused(IntegrationCodes.RunFileUnreadable, "The run file has no patient rows.", serial);
        }

        return new PcrRunInterpretation(true, null, null, serial, samples.Select(sample => sample.Verdict(kit)).ToList());
    }

    /// <summary>The verdict of a complete set of calls (LDM manual §14.6; intake A5).</summary>
    public static PcrVerdict Combine(IReadOnlyList<PcrGeneReading> readings, PcrVerdictRules rules)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(rules);
        var targets = readings.Where(reading => !reading.IsInternalControl).ToList();
        var controls = readings.Where(reading => reading.IsInternalControl).ToList();
        var positives = targets.Count(reading => reading.Call == PcrGeneCall.Positive);

        if (positives >= rules.DetectedMinPositiveTargets)
        {
            return PcrVerdict.Detected;
        }

        if (positives > 0 || targets.Any(reading => reading.Call == PcrGeneCall.Borderline))
        {
            return PcrVerdict.Presumptive;
        }

        return controls.Count == 0 || controls.Any(reading => reading.Call != PcrGeneCall.Negative) ? PcrVerdict.NotDetected : PcrVerdict.Invalid;
    }

    private static string? ReadSerial(IEnumerable<string[]> rows, string? label)
    {
        if (label is null)
        {
            return null;
        }

        foreach (var row in rows)
        {
            for (var index = 0; index < row.Length; index++)
            {
                var cell = row[index].Trim();
                if (string.Equals(cell, label, StringComparison.OrdinalIgnoreCase) || string.Equals(cell.TrimEnd(':', '='), label, StringComparison.OrdinalIgnoreCase))
                {
                    var value = row.Skip(index + 1).Select(next => next.Trim()).FirstOrDefault(next => next.Length > 0);
                    return value;
                }

                if (cell.StartsWith(label, StringComparison.OrdinalIgnoreCase) && cell.Length > label.Length && cell[label.Length] is ':' or '=')
                {
                    return NullIfEmpty(cell[(label.Length + 1)..].Trim());
                }
            }
        }

        return null;
    }

    private static int IndexOf(string[] row, string column)
    {
        for (var index = 0; index < row.Length; index++)
        {
            if (string.Equals(row[index].Trim(), column, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index].Trim() : string.Empty;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>One delimited line with double-quote quoting (<c>""</c> escapes a quote).</summary>
    private static string[] Split(string line, char delimiter)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var c = line[index];
            if (quoted)
            {
                if (c == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return cells.ToArray();
    }

    private sealed class SampleReadings(string sampleNumber, string? well)
    {
        private readonly List<(PcrGene Gene, decimal? Ct)> _readings = [];
        private readonly List<string> _duplicates = [];

        public string SampleNumber { get; } = sampleNumber;

        public void Add(PcrGene gene, decimal? ct)
        {
            if (_readings.Exists(reading => reading.Gene.Id == gene.Id))
            {
                _duplicates.Add(gene.Name);
                return;
            }

            _readings.Add((gene, ct));
        }

        public PcrSampleVerdict Verdict(PcrKit kit)
        {
            var readings = _readings.OrderBy(reading => reading.Gene.Order)
                .Select(reading => new PcrGeneReading(reading.Gene.Name, reading.Ct, reading.Gene.Call(reading.Ct), reading.Gene.IsInternalControl))
                .ToList();
            if (_duplicates.Count > 0)
            {
                return new PcrSampleVerdict(SampleNumber, well, readings, null, null, $"More than one reading for: {string.Join(", ", _duplicates.Distinct(StringComparer.OrdinalIgnoreCase))}.");
            }

            var missing = kit.Genes.Where(gene => !_readings.Exists(reading => reading.Gene.Id == gene.Id)).Select(gene => gene.Name).ToList();
            if (missing.Count > 0)
            {
                return new PcrSampleVerdict(SampleNumber, well, readings, null, null, $"Missing readings for: {string.Join(", ", missing)}.");
            }

            var verdict = Combine(readings, kit.Rules);
            return new PcrSampleVerdict(SampleNumber, well, readings, verdict, kit.Rules.TextFor(verdict), null);
        }
    }
}
