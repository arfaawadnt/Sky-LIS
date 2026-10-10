using System.Text;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Commands;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Persistence.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.PlatformAdmin;

/// <summary>The application rules of platform administration that need no database (intake LIS-INT-PLT-001).</summary>
public sealed class PlatformAdminApplicationTests
{
    [Fact]
    public void INV_PLT_070_only_the_platform_tenant_passes_the_scope_check_PLT_403()
    {
        PlatformAdminScope.Require(new FakeTenant(TenantIds.Platform));

        Assert.Equal(PlatformAdminCodes.PlatformTenantRequired, Assert.Throws<DomainException>(() => PlatformAdminScope.Require(new FakeTenant(Guid.CreateVersion7()))).Code);
        Assert.Equal(PlatformAdminCodes.PlatformTenantRequired, Assert.Throws<DomainException>(() => PlatformAdminScope.Require(new FakeTenant(null))).Code);
    }

    [Theory]
    [InlineData("short1", "noor.admin", 8, PlatformAdminCodes.PasswordTooShort)]
    [InlineData("my-Noor.Admin-2026", "noor.admin", 8, PlatformAdminCodes.PasswordContainsUsername)]
    public void The_password_policy_refuses_with_its_code(string password, string username, int minimum, string code)
    {
        Assert.Equal(code, Assert.Throws<DomainException>(() => ClientPasswordPolicy.Check(password, username, minimum)).Code);
    }

    [Fact]
    public void The_password_policy_has_no_composition_rules_and_a_128_character_ceiling()
    {
        ClientPasswordPolicy.Check("correct horse battery", "noor.admin", 8);   // no digit, no upper case, no symbol: accepted (02 §6.1)
        Assert.Equal(PlatformAdminCodes.PasswordTooShort, Assert.Throws<DomainException>(() => ClientPasswordPolicy.Check(new string('x', 129), "noor.admin", 8)).Code);
        Assert.Equal(PlatformAdminCodes.PasswordTooShort, Assert.Throws<DomainException>(() => ClientPasswordPolicy.Check("twelve-chars", "noor.admin", 13)).Code);
    }

    [Fact]
    public void An_invalid_administrator_username_is_CLI_042()
    {
        Assert.Equal("noor.admin", TenantProvisioner.NormaliseUsername(" Noor.Admin "));
        Assert.Equal(PlatformAdminCodes.UsernameInvalid, Assert.Throws<DomainException>(() => TenantProvisioner.NormaliseUsername("ab")).Code);
    }

    [Theory]
    [InlineData("+201012345678", "+201******678")]
    [InlineData("+96551234567", "+965*****567")]
    [InlineData("12345", "*****")]
    public void The_wizard_masks_the_manager_mobile(string mobile, string expected)
    {
        Assert.Equal(expected, SubscriptionOtp.Mask(mobile));
    }

    [Fact]
    public void Dashboard_periods_have_calendar_boundaries()
    {
        var now = new DateTimeOffset(2026, 10, 9, 15, 30, 0, TimeSpan.Zero);   // a Friday
        Assert.Equal((new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero)), DashboardPeriods.Range("today", now));
        Assert.Equal((new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero)), DashboardPeriods.Range("week", now));
        Assert.Equal((new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)), DashboardPeriods.Range("month", now));
        Assert.Equal((new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)), DashboardPeriods.Range("year", now));
        Assert.Equal("month", DashboardPeriods.Normalise(null));
        Assert.False(DashboardPeriods.IsKnown("decade"));
    }

    [Fact]
    public void The_code_file_reads_quoted_fields_aliases_and_semicolons()
    {
        var rows = CodeSystemCsv.Parse("﻿LOINC_NUM;LONG_COMMON_NAME;STATUS\r\n2345-7;\"Glucose; serum\";ACTIVE\r\n718-7;\"Hemoglobin \"\"Hb\"\"\";ACTIVE\r\n\r\n");

        Assert.Equal([("2345-7", "Glucose; serum"), ("718-7", "Hemoglobin \"Hb\"")], rows);
    }

    [Fact]
    public void The_code_file_keeps_line_breaks_inside_quotes()
    {
        var rows = CodeSystemCsv.Parse("code,display\n\"A1\",\"first line\nsecond line\"\nB2,plain\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("first line\nsecond line", rows[0].Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("name,value\nA,B\n")]
    [InlineData("code,display\n")]
    [InlineData("code,display\nA\n")]
    [InlineData("code,display\n\"A,B\n")]
    public void A_malformed_code_file_is_LIB_010(string text)
    {
        Assert.Equal(PlatformAdminCodes.ImportFileInvalid, Assert.Throws<DomainException>(() => CodeSystemCsv.Parse(text)).Code);
    }

    [Fact]
    public async Task The_code_file_is_read_from_a_stream_as_UTF8()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("code,display\nE11,السكري من النوع الثاني\n"));
        var rows = await CodeSystemCsv.ReadAsync(stream, CancellationToken.None);
        Assert.Equal(("E11", "السكري من النوع الثاني"), Assert.Single(rows));
    }

    [Fact]
    public async Task BR_25_catalogue_A_is_staged_before_the_flush_and_B_applied_after_it_each_elevated_in_the_new_tenant()
    {
        var scope = new RecordingTenantSetter();
        var calls = new List<string>();
        var target = Guid.CreateVersion7();
        var cloner = new MasterDataCloner(new CatalogueA(calls, scope), new CatalogueB(calls, scope), scope);

        using (scope.ForTenant(target))
        {
            await cloner.StageAsync(TenantIds.Platform, target, CancellationToken.None);
            calls.Add("flush");
            await cloner.ApplyAsync(TenantIds.Platform, target, CancellationToken.None);
            Assert.False(scope.IsElevated);   // the elevation ends with each half
            Assert.Equal(target, scope.TenantId);
        }

        Assert.Equal([$"A:{TenantIds.Platform}->{target}:elevated:{target}", "flush", $"B:{TenantIds.Platform}->{target}:elevated:{target}"], calls);
    }

    private sealed class FakeTenant(Guid? tenantId) : ICurrentTenant
    {
        public Guid? TenantId { get; } = tenantId;

        public bool IsElevated => false;
    }

    private sealed class RecordingTenantSetter : ICurrentTenantSetter
    {
        public Guid? TenantId { get; private set; }

        public bool IsElevated { get; private set; }

        public IDisposable ForTenant(Guid tenantId)
        {
            var (tenant, elevated) = (TenantId, IsElevated);
            TenantId = tenantId;
            IsElevated = false;
            return new Restore(() => (TenantId, IsElevated) = (tenant, elevated));
        }

        public IDisposable Elevate()
        {
            var elevated = IsElevated;
            IsElevated = true;
            return new Restore(() => IsElevated = elevated);
        }

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }

    private sealed class CatalogueA(List<string> calls, RecordingTenantSetter scope) : ICatalogueACloner
    {
        public Task<CatalogueACloneResult> CloneAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken) =>
            CloneAsync(sourceTenantId, targetTenantId, null, cancellationToken);

        public Task<CatalogueACloneResult> CloneAsync(Guid sourceTenantId, Guid targetTenantId, IReadOnlyDictionary<Guid, Guid>? reportTemplateMap, CancellationToken cancellationToken)
        {
            calls.Add($"A:{sourceTenantId}->{targetTenantId}:{(scope.IsElevated ? "elevated" : "plain")}:{scope.TenantId}");
            var none = new Dictionary<Guid, Guid>();
            return Task.FromResult(new CatalogueACloneResult(none, none, none, none, none, none, none));
        }
    }

    private sealed class CatalogueB(List<string> calls, RecordingTenantSetter scope) : ICatalogueBCloner
    {
        public Task<CatalogueBCloneSummary> CloneAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken)
        {
            calls.Add($"B:{sourceTenantId}->{targetTenantId}:{(scope.IsElevated ? "elevated" : "plain")}:{scope.TenantId}");
            return Task.FromResult(new CatalogueBCloneSummary(new Dictionary<string, int>(), 0, 0));
        }
    }
}
