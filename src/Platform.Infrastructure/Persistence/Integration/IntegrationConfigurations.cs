using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Identity;
using Platform.Domain.Integration;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.Integration;

// Tables of the Integration module (intake LIS-INT-INT-001 §5). The conventions give every tenant table its
// (tenant_id, id) key, the tenant query filter and xmin concurrency on the roots; RLS is declared in the creating
// migration. Owned children live in their own tables keyed (tenant_id, id) with the composite FK (tenant_id, <root>_id).
// Enum columns of owned types get their domain CHECK here (the context adds them for entity types only).

internal static class IntegrationSql
{
    public static string Domain<TEnum>(string column)
        where TEnum : struct, Enum =>
        $"\"{column}\" IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"))})";
}

/// <summary><c>app.analysers</c> + <c>app.analyser_flags</c>: code unique per tenant (INV-INT-001); the link as columns on the row.</summary>
internal sealed class AnalyserConfiguration : IEntityTypeConfiguration<Analyser>
{
    public void Configure(EntityTypeBuilder<Analyser> builder)
    {
        builder.ToTable("analysers", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_analysers_connection_kind_domain", IntegrationSql.Domain<ConnectionKind>("connection_kind"));
            table.HasCheckConstraint("ck_analysers_tcp_role_domain", "\"tcp_role\" IS NULL OR " + IntegrationSql.Domain<TcpRole>("tcp_role"));
            table.HasCheckConstraint("ck_analysers_print_copies_range", $"print_copies >= 0 AND print_copies <= {Analyser.MaxPrintCopies}");
        });

        builder.Property(analyser => analyser.Code).HasMaxLength(Analyser.MaxCodeLength).IsRequired();
        builder.HasIndex(analyser => new { analyser.TenantId, analyser.Code }).IsUnique().HasDatabaseName("ux_analysers_code");

        builder.OwnsOne(analyser => analyser.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(analyser => analyser.Name).IsRequired();

        builder.OwnsOne(analyser => analyser.Connection, connection =>
        {
            connection.Property(value => value.Kind).HasColumnName("connection_kind").IsRequired();
            connection.Property(value => value.SerialSettings).HasColumnName("serial_settings").HasMaxLength(AnalyserConnection.MaxSerialSettingsLength);
            connection.Property(value => value.SerialPort).HasColumnName("serial_port").HasMaxLength(AnalyserConnection.MaxSerialPortLength);
            connection.Property(value => value.Host).HasColumnName("tcp_host").HasMaxLength(AnalyserConnection.MaxHostLength);
            connection.Property(value => value.Port).HasColumnName("tcp_port");
            connection.Property(value => value.TcpRole).HasColumnName("tcp_role");
            connection.Property(value => value.FolderPath).HasColumnName("folder_path").HasMaxLength(AnalyserConnection.MaxFolderLength);
        });
        builder.Navigation(analyser => analyser.Connection).IsRequired();

        builder.Property(analyser => analyser.ClinicalGroupId).IsRequired();   // app.clinical_groups (catalogue): by id, checked by the handler
        builder.Property(analyser => analyser.Mode).IsRequired();
        builder.Property(analyser => analyser.PrintCopies).IsRequired();
        builder.Property(analyser => analyser.IsActive).IsRequired();
        builder.HasIndex(analyser => new { analyser.TenantId, analyser.SiteAgentId }).HasDatabaseName("ix_analysers_site_agent");
        builder.HasIndex(analyser => new { analyser.TenantId, analyser.ClinicalGroupId }).HasDatabaseName("ix_analysers_clinical_group");

        builder.OwnsMany(analyser => analyser.Flags, flags =>
        {
            flags.ToTable("analyser_flags", AppDbContext.AppSchema, table => table.HasCheckConstraint("ck_analyser_flags_action_domain", IntegrationSql.Domain<FlagAction>("action")));
            flags.WithOwner().HasForeignKey("TenantId", "AnalyserId");
            flags.Property(flag => flag.Id).ValueGeneratedNever();
            flags.HasKey("TenantId", nameof(AnalyserFlag.Id));
            flags.Property(flag => flag.Code).HasMaxLength(Analyser.MaxFlagCodeLength).IsRequired();
            flags.Property(flag => flag.Action).IsRequired();
            flags.Property(flag => flag.Description).HasMaxLength(Analyser.MaxFlagDescriptionLength);
            flags.HasIndex("TenantId", "AnalyserId").HasDatabaseName("ix_analyser_flags_analyser");
        });
        builder.Navigation(analyser => analyser.Flags).HasField("_flags").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>app.analyser_test_mappings</c> + <c>app.analyser_value_conversions</c>: one mapping per analyser code (INV-INT-003).</summary>
internal sealed class AnalyserTestMappingConfiguration : IEntityTypeConfiguration<AnalyserTestMapping>
{
    public void Configure(EntityTypeBuilder<AnalyserTestMapping> builder)
    {
        builder.ToTable("analyser_test_mappings", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint("ck_analyser_test_mappings_slope_non_zero", "slope <> 0"));

        builder.Property(mapping => mapping.AnalyserId).IsRequired();
        builder.HasOne<Analyser>().WithMany().HasForeignKey(mapping => new { mapping.TenantId, mapping.AnalyserId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_analyser_test_mappings_analyser");
        builder.Property(mapping => mapping.TestId).IsRequired();   // app.tests (catalogue): by id, checked by the handler
        builder.Property(mapping => mapping.AnalyserCode).HasMaxLength(AnalyserTestMapping.MaxCodeLength).IsRequired();
        builder.Property(mapping => mapping.HostCode).HasMaxLength(AnalyserTestMapping.MaxCodeLength);
        builder.Property(mapping => mapping.ResultSelection).IsRequired();
        builder.Property(mapping => mapping.Slope).HasPrecision(18, 8).IsRequired();
        builder.Property(mapping => mapping.Intercept).HasPrecision(18, 8).IsRequired();
        builder.Property(mapping => mapping.QueryDetails).HasMaxLength(AnalyserTestMapping.MaxTextLength);
        builder.Property(mapping => mapping.Methodology).HasMaxLength(AnalyserTestMapping.MaxTextLength);
        builder.Property(mapping => mapping.RangeLow).HasPrecision(18, 6);
        builder.Property(mapping => mapping.RangeHigh).HasPrecision(18, 6);
        builder.Property(mapping => mapping.RangeText).HasMaxLength(AnalyserTestMapping.MaxRangeTextLength);
        builder.Property(mapping => mapping.AutoSend).IsRequired();
        builder.Property(mapping => mapping.IsActive).IsRequired();
        builder.Ignore(mapping => mapping.RangeOverride);

        builder.HasIndex(mapping => new { mapping.TenantId, mapping.AnalyserId, mapping.AnalyserCode }).IsUnique().HasDatabaseName("ux_analyser_test_mappings_code");
        builder.HasIndex(mapping => new { mapping.TenantId, mapping.TestId }).HasDatabaseName("ix_analyser_test_mappings_test");

        builder.OwnsMany(mapping => mapping.Conversions, conversions =>
        {
            conversions.ToTable("analyser_value_conversions", AppDbContext.AppSchema);
            conversions.WithOwner().HasForeignKey("TenantId", "MappingId");
            conversions.Property(conversion => conversion.Id).ValueGeneratedNever();
            conversions.HasKey("TenantId", nameof(ValueConversion.Id));
            conversions.Property(conversion => conversion.Order).HasColumnName("sort_order").IsRequired();
            conversions.Property(conversion => conversion.FromValue).HasMaxLength(AnalyserTestMapping.MaxConversionValueLength).IsRequired();
            conversions.Property(conversion => conversion.ToValue).HasMaxLength(AnalyserTestMapping.MaxConversionValueLength).IsRequired();
            conversions.HasIndex("TenantId", "MappingId").HasDatabaseName("ix_analyser_value_conversions_mapping");
        });
        builder.Navigation(mapping => mapping.Conversions).HasField("_conversions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>app.analyser_sample_codes</c>: one code per (analyser, sample type) (INV-INT-006).</summary>
internal sealed class AnalyserSampleCodeConfiguration : IEntityTypeConfiguration<AnalyserSampleCode>
{
    public void Configure(EntityTypeBuilder<AnalyserSampleCode> builder)
    {
        builder.ToTable("analyser_sample_codes", AppDbContext.AppSchema);

        builder.Property(code => code.AnalyserId).IsRequired();
        builder.HasOne<Analyser>().WithMany().HasForeignKey(code => new { code.TenantId, code.AnalyserId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_analyser_sample_codes_analyser");
        builder.Property(code => code.SampleTypeId).IsRequired();   // app.sample_types (catalogue): by id, checked by the handler
        builder.Property(code => code.Code).HasMaxLength(AnalyserSampleCode.MaxCodeLength).IsRequired();
        builder.Property(code => code.IsActive).IsRequired();
        builder.HasIndex(code => new { code.TenantId, code.AnalyserId, code.SampleTypeId }).IsUnique().HasDatabaseName("ux_analyser_sample_codes_type");
    }
}

/// <summary><c>app.site_agents</c>: name unique per tenant (INV-INT-007); the credential lives in <c>identity.machine_credentials</c>.</summary>
internal sealed class SiteAgentConfiguration : IEntityTypeConfiguration<SiteAgent>
{
    public void Configure(EntityTypeBuilder<SiteAgent> builder)
    {
        builder.ToTable("site_agents", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint("ck_site_agents_config_version_positive", "config_version >= 1"));

        builder.Property(agent => agent.Name).HasMaxLength(SiteAgent.MaxNameLength).IsRequired();
        builder.HasIndex(agent => new { agent.TenantId, agent.Name }).IsUnique().HasDatabaseName("ux_site_agents_name");
        builder.Property(agent => agent.BranchId).IsRequired();   // app.branches (organization): by id, checked by the handler
        builder.Property(agent => agent.MachineCredentialId).IsRequired();
        builder.HasOne<MachineCredential>().WithMany().HasForeignKey(agent => agent.MachineCredentialId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_site_agents_machine_credential");
        builder.Property(agent => agent.ServiceUserId).IsRequired();
        builder.HasIndex(agent => agent.ServiceUserId).HasDatabaseName("ix_site_agents_service_user");
        builder.Property(agent => agent.ConfigJson).HasColumnType("jsonb").IsRequired();
        builder.Property(agent => agent.ConfigVersion).IsRequired();
        builder.Property(agent => agent.IsActive).IsRequired();
        builder.HasIndex(agent => new { agent.TenantId, agent.BranchId }).HasDatabaseName("ix_site_agents_branch");
    }
}

/// <summary><c>app.pcr_kits</c> + <c>app.pcr_kit_genes</c>: name unique per tenant; layout and verdict rules as columns on the row.</summary>
internal sealed class PcrKitConfiguration : IEntityTypeConfiguration<PcrKit>
{
    public void Configure(EntityTypeBuilder<PcrKit> builder)
    {
        builder.ToTable("pcr_kits", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint("ck_pcr_kits_detected_min_targets_range", $"detected_min_positive_targets >= 1 AND detected_min_positive_targets <= {PcrVerdictRules.MaxTargets}"));

        builder.Property(kit => kit.Name).HasMaxLength(PcrKit.MaxNameLength).IsRequired();
        builder.HasIndex(kit => new { kit.TenantId, kit.Name }).IsUnique().HasDatabaseName("ux_pcr_kits_name");
        builder.Property(kit => kit.AnalyserId).IsRequired();
        builder.HasOne<Analyser>().WithMany().HasForeignKey(kit => new { kit.TenantId, kit.AnalyserId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pcr_kits_analyser");
        builder.Property(kit => kit.AnalyserTestCode).HasMaxLength(AnalyserTestMapping.MaxCodeLength).IsRequired();
        builder.Property(kit => kit.SerialLock).HasMaxLength(PcrKit.MaxSerialLength);
        builder.Property(kit => kit.ValueColumn).HasMaxLength(PcrKit.MaxColumnLength).IsRequired();
        builder.Property(kit => kit.ResultFolder).HasMaxLength(PcrKit.MaxFolderLength).IsRequired();
        builder.Property(kit => kit.BackupFolder).HasMaxLength(PcrKit.MaxFolderLength).IsRequired();
        builder.Property(kit => kit.FailedFolder).HasMaxLength(PcrKit.MaxFolderLength).IsRequired();
        builder.Property(kit => kit.IsActive).IsRequired();

        builder.OwnsOne(kit => kit.Layout, layout =>
        {
            layout.Property(value => value.Delimiter).HasColumnName("layout_delimiter").HasMaxLength(1).IsRequired();
            layout.Property(value => value.SampleColumn).HasColumnName("layout_sample_column").HasMaxLength(PcrKit.MaxColumnLength).IsRequired();
            layout.Property(value => value.GeneColumn).HasColumnName("layout_gene_column").HasMaxLength(PcrKit.MaxColumnLength).IsRequired();
            layout.Property(value => value.WellColumn).HasColumnName("layout_well_column").HasMaxLength(PcrKit.MaxColumnLength);
            layout.Property(value => value.ContentColumn).HasColumnName("layout_content_column").HasMaxLength(PcrKit.MaxColumnLength);
            layout.Property(value => value.PatientContentValue).HasColumnName("layout_patient_content_value").HasMaxLength(PcrKit.MaxColumnLength);
            layout.Property(value => value.SerialLabel).HasColumnName("layout_serial_label").HasMaxLength(PcrRunFileLayout.MaxLabelLength);
            layout.Property(value => value.NoAmplificationTokens).HasColumnName("layout_no_amplification_tokens").IsRequired();
            layout.Ignore(value => value.DelimiterChar);
        });
        builder.Navigation(kit => kit.Layout).IsRequired();

        builder.OwnsOne(kit => kit.Rules, rules =>
        {
            rules.Property(value => value.DetectedMinPositiveTargets).HasColumnName("detected_min_positive_targets").IsRequired();
            rules.Property(value => value.DetectedText).HasColumnName("detected_text").HasMaxLength(PcrVerdictRules.MaxTextLength).IsRequired();
            rules.Property(value => value.NotDetectedText).HasColumnName("not_detected_text").HasMaxLength(PcrVerdictRules.MaxTextLength).IsRequired();
            rules.Property(value => value.PresumptiveText).HasColumnName("presumptive_text").HasMaxLength(PcrVerdictRules.MaxTextLength).IsRequired();
            rules.Property(value => value.InvalidText).HasColumnName("invalid_text").HasMaxLength(PcrVerdictRules.MaxTextLength).IsRequired();
        });
        builder.Navigation(kit => kit.Rules).IsRequired();

        builder.OwnsMany(kit => kit.Genes, genes =>
        {
            genes.ToTable("pcr_kit_genes", AppDbContext.AppSchema, table =>
                table.HasCheckConstraint("ck_pcr_kit_genes_thresholds", $"positive_ct_max > 0 AND positive_ct_max <= borderline_ct_max AND borderline_ct_max <= {PcrKit.MaxCt}"));
            genes.WithOwner().HasForeignKey("TenantId", "KitId");
            genes.Property(gene => gene.Id).ValueGeneratedNever();
            genes.HasKey("TenantId", nameof(PcrGene.Id));
            genes.Property(gene => gene.Order).HasColumnName("sort_order").IsRequired();
            genes.Property(gene => gene.Name).HasMaxLength(PcrKit.MaxGeneNameLength).IsRequired();
            genes.Property(gene => gene.IsInternalControl).IsRequired();
            genes.Property(gene => gene.PositiveCtMax).HasPrecision(6, 3).IsRequired();
            genes.Property(gene => gene.BorderlineCtMax).HasPrecision(6, 3).IsRequired();
            genes.HasIndex("TenantId", "KitId").HasDatabaseName("ix_pcr_kit_genes_kit");
        });
        builder.Navigation(kit => kit.Genes).HasField("_genes").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SiteAgentStateRowConfiguration : IEntityTypeConfiguration<SiteAgentStateRow>
{
    public void Configure(EntityTypeBuilder<SiteAgentStateRow> builder)
    {
        builder.ToTable("site_agent_states", AppDbContext.AppSchema);
        builder.Property(row => row.Version).HasMaxLength(SiteAgentStateRow.MaxVersionLength);
        builder.Property(row => row.Message).HasMaxLength(SiteAgentStateRow.MaxMessageLength);
        builder.Property(row => row.ReportedError).IsRequired();
        builder.Property(row => row.LastHeartbeatAtUtc).IsRequired();
    }
}

internal sealed class AnalyserConnectionStateRowConfiguration : IEntityTypeConfiguration<AnalyserConnectionStateRow>
{
    public void Configure(EntityTypeBuilder<AnalyserConnectionStateRow> builder)
    {
        builder.ToTable("analyser_connection_states", AppDbContext.AppSchema);
        builder.Property(row => row.SiteAgentId).IsRequired();
        builder.Property(row => row.Status).IsRequired();
        builder.Property(row => row.Message).HasMaxLength(SiteAgentStateRow.MaxMessageLength);
        builder.Property(row => row.LastSeenAtUtc).IsRequired();
    }
}

internal sealed class AgentLogEntryRowConfiguration : IEntityTypeConfiguration<AgentLogEntryRow>
{
    public void Configure(EntityTypeBuilder<AgentLogEntryRow> builder)
    {
        builder.ToTable("agent_log_entries", AppDbContext.AppSchema);
        builder.Property(row => row.AgentId).IsRequired();
        builder.Property(row => row.AtUtc).IsRequired();
        builder.Property(row => row.Level).IsRequired();
        builder.Property(row => row.Message).HasMaxLength(AgentLogEntryRow.MaxMessageLength).IsRequired();
        builder.Property(row => row.SampleNumber).HasMaxLength(AgentLogEntryRow.MaxSampleNumberLength);
        builder.Property(row => row.AnalyserCode).HasMaxLength(Analyser.MaxCodeLength);
        builder.Property(row => row.ReceivedAtUtc).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.AgentId, row.AtUtc }).HasDatabaseName("ix_agent_log_entries_agent_time");
    }
}

internal sealed class ResultIntakeRowConfiguration : IEntityTypeConfiguration<ResultIntakeRow>
{
    public void Configure(EntityTypeBuilder<ResultIntakeRow> builder)
    {
        builder.ToTable("analyser_result_intake", AppDbContext.AppSchema);
        builder.Property(row => row.AgentId).IsRequired();
        builder.Property(row => row.AnalyserCode).HasMaxLength(Analyser.MaxCodeLength).IsRequired();
        builder.Property(row => row.SampleNumber).HasMaxLength(ResultIntakeRow.MaxSampleNumberLength).IsRequired();
        builder.Property(row => row.AnalyserTestCode).HasMaxLength(AnalyserTestMapping.MaxCodeLength).IsRequired();
        builder.Property(row => row.RawValue).HasMaxLength(ResultIntakeRow.MaxValueLength).IsRequired();
        builder.Property(row => row.ConvertedValue).HasMaxLength(ResultIntakeRow.MaxValueLength);
        builder.Property(row => row.Flags).IsRequired();
        builder.Property(row => row.Status).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(ResultIntakeRow.MaxReasonLength);
        builder.Property(row => row.Source).HasMaxLength(ResultIntakeRow.MaxSourceLength).IsRequired();
        builder.Property(row => row.FileName).HasMaxLength(ResultIntakeRow.MaxFileNameLength);
        builder.Property(row => row.AtUtc).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.AtUtc }).HasDatabaseName("ix_analyser_result_intake_time");
        builder.HasIndex(row => new { row.TenantId, row.AnalyserId, row.AtUtc }).HasDatabaseName("ix_analyser_result_intake_analyser");
        builder.HasIndex(row => new { row.TenantId, row.Status, row.AtUtc }).HasDatabaseName("ix_analyser_result_intake_status");
        builder.HasIndex(row => new { row.TenantId, row.SampleNumber }).HasDatabaseName("ix_analyser_result_intake_sample");
    }
}
