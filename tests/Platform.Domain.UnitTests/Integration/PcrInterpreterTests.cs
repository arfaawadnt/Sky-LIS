using Platform.Domain.Integration;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Integration;

/// <summary>
/// Scenarios K1 and R2 of intake LIS-INT-INT-001 (S-K-verdicts, S-K-serial) with the BioRad CFX + SOLGENT example of LDM
/// manual §14.3–§14.6: ORF1a / RdRp / N targets positive at Ct ≤ 38, borderline to 40; IC positive at Ct ≤ 40.
/// </summary>
public sealed class PcrInterpreterTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private const string Serial = "785BR29753";

    internal static PcrKitDefinition Definition(int minTargets = 1, string? serialLock = Serial, IReadOnlyList<PcrGeneSpec>? genes = null) =>
        new(
            "Biorad_Solgent",
            "COVID",
            serialLock,
            PcrRunFileLayout.Create(",", "Sample", "Target", "Well", "Content", "Unkn", "Optical Head Serial Number", ["N/A", "Undetermined"]),
            "Cq",
            genes ??
            [
                new PcrGeneSpec("ORF1a", false, 38m, 40m),
                new PcrGeneSpec("RdRp", false, 38m, 40m),
                new PcrGeneSpec("N Gene", false, 38m, 40m),
                new PcrGeneSpec("IC", true, 40m, 40m),
            ],
            PcrVerdictRules.Create(minTargets, "DETECTED", "NOT DETECTED", "Presumptive positive", "INVALID"),
            @"D:\COMM\Results\Biorad_Solgent_1",
            @"D:\COMM\Results\Backup",
            @"D:\COMM\Fail");

    internal static PcrKit Kit(int minTargets = 1, string? serialLock = Serial) =>
        PcrKit.Define(Analyser.Create("BIORAD", AnalyserTests.Definition(AnalyserMode.SharedFolder, AnalyserConnection.File(@"D:\COMM\Results")), Now), Definition(minTargets, serialLock), Now);

    internal static string RunFile(string serial = Serial, params (string Well, string Sample, string Orf, string Rdrp, string N, string Ic)[] samples)
    {
        var lines = new List<string>
        {
            "File Name,run-2026-10-09.pcrd",
            $"Optical Head Serial Number,{serial}",
            string.Empty,
            "Well,Fluor,Target,Content,Sample,Cq",
            "H12,VIC,ORF1a,Pos Ctrl,PC,25.10",
            "H12,CY5,RdRp,Pos Ctrl,PC,26.00",
        };
        foreach (var sample in samples)
        {
            lines.Add($"{sample.Well},VIC,ORF1a,Unkn,{sample.Sample},{sample.Orf}");
            lines.Add($"{sample.Well},CY5,RdRp,Unkn,{sample.Sample},{sample.Rdrp}");
            lines.Add($"{sample.Well},FAM,N Gene,Unkn,{sample.Sample},{sample.N}");
            lines.Add($"{sample.Well},Cal Red 610,IC,Unkn,{sample.Sample},{sample.Ic}");
        }

        return string.Join("\r\n", lines);
    }

    [Fact]
    public void K_verdicts_follow_the_interpretation_sheet()
    {
        var kit = Kit();
        var file = RunFile(
            Serial,
            ("B03", "SMP-1", "35.17", "34.63", "33.33", "30.1"),   // targets positive → DETECTED (log shows R=DETECTED)
            ("A02", "SMP-2", "0.00", "", "N/A", "31.2"),           // targets negative, IC present → NOT DETECTED
            ("A03", "SMP-3", "39.10", "", "", "30.0"),             // borderline target → Presumptive
            ("A04", "SMP-4", "", "", "Undetermined", "0"),         // all four negative incl. IC → INVALID
            ("A05", "SMP-5", "41", "45", "", "42"));                // above 40 is negative; IC above 40 → INVALID

        var run = kit.Interpret(null, file);

        Assert.True(run.Readable);
        Assert.Equal(RunFileDisposition.Backup, run.Disposition);
        Assert.Equal(Serial, run.FileSerial);
        Assert.Equal(
            [("SMP-1", PcrVerdict.Detected, "DETECTED"), ("SMP-2", PcrVerdict.NotDetected, "NOT DETECTED"), ("SMP-3", PcrVerdict.Presumptive, "Presumptive positive"), ("SMP-4", PcrVerdict.Invalid, "INVALID"), ("SMP-5", PcrVerdict.Invalid, "INVALID")],
            run.Samples.Select(sample => (sample.SampleNumber, sample.Verdict!.Value, sample.VerdictText!)).ToArray());
        Assert.Equal(["B03-ORF1a-35.17", "B03-RdRp-34.63", "B03-N Gene-33.33", "B03-IC-30.1"], run.Samples[0].Trail);
    }

    [Theory]
    [InlineData("35", "", "", PcrGeneCall.Positive)]
    [InlineData("38", "", "", PcrGeneCall.Positive)]
    [InlineData("38.01", "", "", PcrGeneCall.Borderline)]
    [InlineData("40", "", "", PcrGeneCall.Borderline)]
    [InlineData("40.01", "", "", PcrGeneCall.Negative)]
    [InlineData("0", "", "", PcrGeneCall.Negative)]
    public void A_low_Ct_is_positive(string orf, string rdrp, string n, PcrGeneCall expected)
    {
        var run = Kit().Interpret(Serial, RunFile(Serial, ("A01", "S", orf, rdrp, n, "30")));
        Assert.Equal(expected, run.Samples.Single().Genes.Single(gene => gene.Gene == "ORF1a").Call);
    }

    [Fact]
    public void One_positive_target_is_presumptive_when_the_kit_requires_two()
    {
        var run = Kit(minTargets: 2).Interpret(Serial, RunFile(Serial, ("A01", "S1", "30", "", "", "30"), ("A02", "S2", "30", "31", "", "30")));

        Assert.Equal([PcrVerdict.Presumptive, PcrVerdict.Detected], run.Samples.Select(sample => sample.Verdict!.Value).ToArray());
    }

    [Fact]
    public void Controls_are_skipped_and_only_patient_rows_are_read()
    {
        var run = Kit().Interpret(Serial, RunFile(Serial, ("A01", "S1", "", "", "", "30")));
        Assert.Equal(["S1"], run.Samples.Select(sample => sample.SampleNumber).ToArray());
    }

    [Fact]
    public void S_K_serial_a_file_from_another_instrument_is_refused_INT_037()
    {
        var run = Kit().Interpret(null, RunFile("999XX00000", ("A01", "S1", "30", "", "", "30")));

        Assert.False(run.Readable);
        Assert.Equal(RunFileDisposition.Failed, run.Disposition);
        Assert.Equal(IntegrationCodes.RunFileFromOtherInstrument, run.RefusalCode);
        Assert.Empty(run.Samples);
    }

    [Fact]
    public void A_declared_serial_that_contradicts_the_file_is_refused_INT_037()
    {
        var run = Kit(serialLock: null).Interpret("OTHER-SERIAL", RunFile(Serial, ("A01", "S1", "30", "", "", "30")));
        Assert.Equal(IntegrationCodes.RunFileFromOtherInstrument, run.RefusalCode);
    }

    [Fact]
    public void Without_a_serial_line_the_declared_serial_is_checked_against_the_lock()
    {
        var file = RunFile(Serial, ("A01", "S1", "30", "", "", "30")).Replace("Optical Head Serial Number", "Something Else", StringComparison.Ordinal);

        Assert.True(Kit().Interpret(Serial, file).Readable);
        Assert.Equal(IntegrationCodes.RunFileFromOtherInstrument, Kit().Interpret(null, file).RefusalCode);
    }

    [Fact]
    public void An_unknown_gene_fails_the_file_like_a_renamed_gene_INT_038()
    {
        var file = RunFile(Serial, ("A01", "S1", "30", "", "", "30")).Replace(",N Gene,", ",N-gene,", StringComparison.Ordinal);

        var run = Kit().Interpret(Serial, file);
        Assert.Equal(IntegrationCodes.RunFileUnreadable, run.RefusalCode);
        Assert.Contains("N-gene", run.RefusalReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no,header,here\n1,2,3")]
    public void An_empty_file_or_one_without_the_header_fails_INT_038(string content)
    {
        Assert.Equal(IntegrationCodes.RunFileUnreadable, Kit().Interpret(Serial, content).RefusalCode);
    }

    [Fact]
    public void A_value_that_is_not_a_Ct_fails_the_file_INT_038()
    {
        var run = Kit().Interpret(Serial, RunFile(Serial, ("A01", "S1", "abc", "", "", "30")));
        Assert.Equal(IntegrationCodes.RunFileUnreadable, run.RefusalCode);
    }

    [Fact]
    public void A_sample_missing_a_gene_gets_no_verdict()
    {
        var lines = RunFile(Serial, ("A01", "S1", "30", "", "", "30")).Split("\r\n").Where(line => !line.Contains(",IC,Unkn,", StringComparison.Ordinal));
        var run = Kit().Interpret(Serial, string.Join("\n", lines));

        var sample = Assert.Single(run.Samples);
        Assert.Null(sample.Verdict);
        Assert.Contains("IC", sample.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Quoted_cells_and_semicolons_are_read()
    {
        var kit = PcrKit.Define(
            Analyser.Create("QS5", AnalyserTests.Definition(AnalyserMode.SharedFolder, AnalyserConnection.File(@"D:\COMM\QS")), Now),
            Definition(serialLock: "272512345") with
            {
                Layout = PcrRunFileLayout.Create(";", "Sample Name", "Target Name", "Well Position", null, null, "Instrument Serial Number", ["Undetermined"]),
                ValueColumn = "CT",
                Genes = [new PcrGeneSpec("Neo-N gene", false, 38m, 40m), new PcrGeneSpec("Neo-RdRp", false, 38m, 40m), new PcrGeneSpec("Neo-IC", true, 40m, 40m)],
            },
            Now);
        const string File = "Instrument Serial Number: 272512345\nWell;Well Position;Sample Name;Target Name;CT\n1;A1;\"SMP-9\";\"Neo-N gene\";Undetermined\n1;A1;SMP-9;Neo-RdRp;Undetermined\n1;A1;SMP-9;Neo-IC;28.5";

        var run = kit.Interpret(null, File);

        Assert.True(run.Readable);
        var sample = Assert.Single(run.Samples);
        Assert.Equal(("SMP-9", "A1", (PcrVerdict?)PcrVerdict.NotDetected), (sample.SampleNumber, sample.Well, sample.Verdict));
    }

    [Fact]
    public void K1_A_kit_needs_a_target_gene_and_ordered_thresholds_INT_022()
    {
        var analyser = Analyser.Create("BIORAD", AnalyserTests.Definition(AnalyserMode.SharedFolder, AnalyserConnection.File(@"D:\COMM\Results")), Now);

        Assert.Equal(IntegrationCodes.GenesInvalid, Assert.Throws<DomainException>(() => PcrKit.Define(analyser, Definition(genes: [new PcrGeneSpec("IC", true, 40m, 40m)]), Now)).Code);
        Assert.Equal(IntegrationCodes.GenesInvalid, Assert.Throws<DomainException>(() => PcrKit.Define(analyser, Definition(genes: [new PcrGeneSpec("N", false, 40m, 38m)]), Now)).Code);
        Assert.Equal(IntegrationCodes.GenesInvalid, Assert.Throws<DomainException>(() => PcrKit.Define(analyser, Definition(genes: [new PcrGeneSpec("N", false, 38m, 40m), new PcrGeneSpec("n", false, 38m, 40m)]), Now)).Code);
        Assert.Equal(IntegrationCodes.FoldersInvalid, Assert.Throws<DomainException>(() => PcrKit.Define(analyser, Definition() with { BackupFolder = @"D:\COMM\Fail" }, Now)).Code);
        Assert.Equal(IntegrationCodes.VerdictRulesInvalid, Assert.Throws<DomainException>(() => PcrVerdictRules.Create(0, "D", "N", "P", "I")).Code);
        Assert.Equal(IntegrationCodes.RunFileLayoutInvalid, Assert.Throws<DomainException>(() => PcrRunFileLayout.Create("#", "S", "G", null, null, null, null, null)).Code);
    }

    [Fact]
    public void K1_Define_raises_PcrKitDefined_and_deactivation_is_idempotent()
    {
        var kit = Kit();
        Assert.IsType<PcrKitDefined>(Assert.Single(kit.DomainEvents));
        kit.ClearDomainEvents();

        kit.Deactivate(Now);
        kit.Deactivate(Now);
        kit.Reactivate(Now);

        Assert.Collection(kit.DomainEvents, e => Assert.IsType<PcrKitDeactivated>(e), e => Assert.IsType<PcrKitReactivated>(e));
    }

    [Fact]
    public void Combine_without_an_internal_control_reads_all_negative_as_not_detected()
    {
        var readings = new List<PcrGeneReading> { new("N", null, PcrGeneCall.Negative, false) };
        Assert.Equal(PcrVerdict.NotDetected, PcrInterpreter.Combine(readings, PcrVerdictRules.Default));
    }
}
