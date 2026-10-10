using Platform.Domain.Integration;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Integration;

/// <summary>Scenarios A1–A3 and R1 of intake LIS-INT-INT-001 §7/§16 on the Analyser aggregate.</summary>
public sealed class AnalyserTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Group = Guid.CreateVersion7();

    internal static AnalyserDefinition Definition(
        AnalyserMode mode = AnalyserMode.Unidirectional,
        AnalyserConnection? connection = null,
        IReadOnlyList<AnalyserFlagSpec>? flags = null,
        AnalyserSwitches? switches = null,
        int copies = 1) =>
        new(
            new LocalizedText("Sysmex XN-550", "سيسمكس"),
            Group,
            null,
            null,
            mode,
            connection ?? AnalyserConnection.Serial("9600,n,8,1", "COM3"),
            switches ?? new AnalyserSwitches(false, false, false, false, false, false),
            copies,
            flags ?? [new AnalyserFlagSpec("H", FlagAction.Record, "High"), new AnalyserFlagSpec("CLOT", FlagAction.Stop, "Clot detected")],
            null);

    internal static Analyser Active(AnalyserSwitches? switches = null) => Analyser.Create("xn550", Definition(switches: switches), Now);

    [Fact]
    public void A1_Create_happy_path_upper_cases_the_code_and_raises_AnalyserRegistered()
    {
        var analyser = Analyser.Create(" xn-550 ", Definition(), Now);

        Assert.Equal("XN-550", analyser.Code);
        Assert.True(analyser.IsActive);
        Assert.Equal(ConnectionKind.Serial, analyser.Connection.Kind);
        Assert.Equal(("9600,n,8,1", "COM3"), (analyser.Connection.SerialSettings, analyser.Connection.SerialPort));
        Assert.Equal(2, analyser.Flags.Count);
        var raised = Assert.IsType<AnalyserRegistered>(Assert.Single(analyser.DomainEvents));
        Assert.Equal((analyser.Id, "XN-550", Group), (raised.AnalyserId, raised.Code, raised.ClinicalGroupId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("ABCDEFGHIJKLMNOPQ")]
    public void A1_Create_refuses_an_invalid_code_INT_001(string code)
    {
        var refusal = Assert.Throws<DomainException>(() => Analyser.Create(code, Definition(), Now));
        Assert.Equal(IntegrationCodes.CodeInvalid, refusal.Code);
    }

    [Theory]
    [InlineData("9600-n-8-1")]
    [InlineData("9600,x,8,1")]
    [InlineData("9600,n,9,1")]
    [InlineData("9600,n,8,3")]
    [InlineData("100,n,8,1")]
    [InlineData("")]
    public void A1_Serial_settings_must_look_like_9600_n_8_1_INT_005(string settings)
    {
        var refusal = Assert.Throws<DomainException>(() => AnalyserConnection.Serial(settings, "COM3"));
        Assert.Equal(IntegrationCodes.SerialSettingsInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Theory]
    [InlineData("9600,N,8,1", "com4", "9600,n,8,1", "COM4")]
    [InlineData(" 115200, e, 7, 2 ", "/dev/ttyS0", "115200,e,7,2", "/dev/ttyS0")]
    [InlineData("19200,n,8,1.5", "COM12", "19200,n,8,1.5", "COM12")]
    public void A1_Serial_settings_are_normalised(string settings, string port, string expectedSettings, string expectedPort)
    {
        var connection = AnalyserConnection.Serial(settings, port);
        Assert.Equal((expectedSettings, expectedPort), (connection.SerialSettings, connection.SerialPort));
    }

    [Fact]
    public void A1_Tcp_needs_a_port_and_a_host_for_a_client_INT_006()
    {
        Assert.Equal(IntegrationCodes.TcpEndpointInvalid, Assert.Throws<DomainException>(() => AnalyserConnection.Tcp("10.0.0.5", 0, TcpRole.Client)).Code);
        Assert.Equal(IntegrationCodes.TcpEndpointInvalid, Assert.Throws<DomainException>(() => AnalyserConnection.Tcp(null, 5000, TcpRole.Client)).Code);
        Assert.Equal(IntegrationCodes.TcpEndpointInvalid, Assert.Throws<DomainException>(() => AnalyserConnection.Tcp("bad host!", 5000, TcpRole.Client)).Code);

        var server = AnalyserConnection.Tcp(null, 5000, TcpRole.Server);
        Assert.Equal((ConnectionKind.Tcp, (int?)5000, (TcpRole?)TcpRole.Server, (string?)null), (server.Kind, server.Port, server.TcpRole, server.Host));
    }

    [Fact]
    public void A1_Shared_folder_mode_and_file_link_go_together_INT_004()
    {
        var mismatch = Assert.Throws<DomainException>(() => Analyser.Create("PCR1", Definition(AnalyserMode.SharedFolder, AnalyserConnection.Serial("9600,n,8,1", "COM1")), Now));
        Assert.Equal(IntegrationCodes.ModeConnectionMismatch, mismatch.Code);

        var reverse = Assert.Throws<DomainException>(() => Analyser.Create("PCR1", Definition(AnalyserMode.Bidirectional, AnalyserConnection.File(@"D:\COMM\Results")), Now));
        Assert.Equal(IntegrationCodes.ModeConnectionMismatch, reverse.Code);

        var ok = Analyser.Create("PCR1", Definition(AnalyserMode.SharedFolder, AnalyserConnection.File(@"D:\COMM\Results\Biorad")), Now);
        Assert.Equal(@"D:\COMM\Results\Biorad", ok.Connection.FolderPath);
    }

    [Fact]
    public void A1_Download_mode_always_downloads_orders()
    {
        var analyser = Analyser.Create("COBAS", Definition(AnalyserMode.Download, AnalyserConnection.Tcp("10.1.1.20", 2000, TcpRole.Client)), Now);
        Assert.True(analyser.DownloadOrders);
    }

    [Fact]
    public void A1_Flags_are_unique_and_print_copies_bounded_INT_009_INT_008()
    {
        var duplicate = Assert.Throws<DomainException>(() => Analyser.Create("A1", Definition(flags: [new AnalyserFlagSpec("H", FlagAction.Record, null), new AnalyserFlagSpec("h", FlagAction.Stop, null)]), Now));
        Assert.Equal(IntegrationCodes.FlagsInvalid, duplicate.Code);

        var copies = Assert.Throws<DomainException>(() => Analyser.Create("A1", Definition(copies: 11), Now));
        Assert.Equal(IntegrationCodes.PrintCopiesInvalid, copies.Code);
    }

    [Fact]
    public void A2_Update_keeps_existing_flag_rows_and_drops_removed_ones()
    {
        var analyser = Active();
        var clot = analyser.Flags.Single(flag => flag.Code == "CLOT");

        analyser.Update(Definition(flags: [new AnalyserFlagSpec("CLOT", FlagAction.Record, "Clot (record only)"), new AnalyserFlagSpec("LIP", FlagAction.Stop, null)]));

        Assert.Equal(["CLOT", "LIP"], analyser.Flags.Select(flag => flag.Code).Order(StringComparer.Ordinal).ToArray());
        Assert.Same(clot, analyser.Flags.Single(flag => flag.Code == "CLOT"));
        Assert.Equal(FlagAction.Record, clot.Action);
    }

    [Fact]
    public void A3_Deactivate_and_reactivate_are_idempotent_and_announced()
    {
        var analyser = Active();
        analyser.ClearDomainEvents();

        analyser.Deactivate(Now);
        analyser.Deactivate(Now);
        Assert.False(analyser.IsActive);
        analyser.Reactivate(Now);
        analyser.Reactivate(Now);

        Assert.True(analyser.IsActive);
        Assert.Collection(analyser.DomainEvents, e => Assert.IsType<AnalyserDeactivated>(e), e => Assert.IsType<AnalyserReactivated>(e));
    }

    [Fact]
    public void R1_Accepted_result_applies_the_mapping_and_raises_AnalyserResultReceived_with_stop_flags()
    {
        var analyser = Active(new AnalyserSwitches(true, false, true, false, true, false));
        var mapping = AnalyserTestMappingTests.Mapping(analyser, autoSend: true, slope: 2m);
        analyser.ClearDomainEvents();
        var intake = Guid.CreateVersion7();
        var sample = Guid.CreateVersion7();

        var outcome = analyser.AcceptResult(mapping, new IncomingAnalyserResult("SMP-2026-00001", sample, "WBC", "4.5", ["clot", "H", "H"], "10^3/uL", Now), intake, ResultSources.Analyser, Now);

        Assert.Equal(ResultIntakeStatus.Accepted, outcome.Status);
        Assert.True(outcome.Landed);
        Assert.Equal("9", outcome.ConvertedValue);
        Assert.Equal(["clot"], outcome.StopFlags);
        var raised = Assert.IsType<AnalyserResultReceived>(Assert.Single(analyser.DomainEvents));
        Assert.Equal((analyser.Id, "XN550", "SMP-2026-00001", (Guid?)sample, mapping.TestId, mapping.Id), (raised.AnalyserId, raised.AnalyserCode, raised.SampleNumber, raised.SampleId, raised.TestId, raised.MappingId));
        Assert.Equal(("9", "4.5", "10^3/uL", intake, ResultSources.Analyser), (raised.Value, raised.RawValue, raised.Unit, raised.IntakeId, raised.Source));
        Assert.Equal(["clot", "H"], raised.Flags);
        Assert.Equal(["clot"], raised.StopFlags);
        Assert.True(raised.OverwriteReviewed);
        Assert.False(raised.OverwriteVerified);
        Assert.True(raised.AppendUnregisteredTests);
    }

    [Fact]
    public void R1_A_mapping_without_Auto_Send_never_lands_a_result_INV_INT_004()
    {
        var analyser = Active();
        var mapping = AnalyserTestMappingTests.Mapping(analyser, autoSend: false);
        analyser.ClearDomainEvents();

        var outcome = analyser.AcceptResult(mapping, new IncomingAnalyserResult("S1", Guid.CreateVersion7(), "WBC", "4.5", [], null, Now), Guid.CreateVersion7(), ResultSources.Analyser, Now);

        Assert.Equal(ResultIntakeStatus.AutoSendOff, outcome.Status);
        Assert.Equal("4.5", outcome.ConvertedValue);
        Assert.Empty(analyser.DomainEvents);
    }

    [Fact]
    public void R1_Inactive_analyser_refuses_and_inactive_mapping_is_unmapped()
    {
        var analyser = Active();
        var mapping = AnalyserTestMappingTests.Mapping(analyser, autoSend: true);
        analyser.ClearDomainEvents();
        var incoming = new IncomingAnalyserResult("S1", null, "WBC", "1", [], null, Now);

        mapping.Deactivate();
        Assert.Equal(ResultIntakeStatus.Unmapped, analyser.AcceptResult(mapping, incoming, Guid.CreateVersion7(), ResultSources.Analyser, Now).Status);

        mapping.Reactivate();
        analyser.Deactivate(Now);
        analyser.ClearDomainEvents();
        Assert.Equal(ResultIntakeStatus.Refused, analyser.AcceptResult(mapping, incoming, Guid.CreateVersion7(), ResultSources.Analyser, Now).Status);
        Assert.Empty(analyser.DomainEvents);
    }

    [Fact]
    public void R1_A_mapping_of_another_analyser_is_refused_INT_033()
    {
        var analyser = Active();
        var other = Analyser.Create("OTHER", Definition(), Now);
        var mapping = AnalyserTestMappingTests.Mapping(other, autoSend: true);

        var refusal = Assert.Throws<DomainException>(() => analyser.AcceptResult(mapping, new IncomingAnalyserResult("S1", null, "WBC", "1", [], null, Now), Guid.CreateVersion7(), ResultSources.Analyser, Now));
        Assert.Equal(IntegrationCodes.MappingOfAnotherAnalyser, refusal.Code);
    }
}
