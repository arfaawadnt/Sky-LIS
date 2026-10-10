using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>Notification templates (INV-PLT-040), the integration library (INV-PLT-050…054) and the wizard request (INV-PLT-060).</summary>
public sealed class TemplateLibrarySubscriptionTests
{
    [Fact]
    public void A_template_accepts_known_placeholders_and_aliases()
    {
        var template = NotificationTemplate.Create(" Portal-URL ", TemplateChannel.Sms, "{lab}: open {url} as {username}", "{lab}: افتح {url} باسم {username}", null, null);

        Assert.Equal("portal-url", template.Code);
        Assert.Equal(["lab", "url", "username"], NotificationTemplate.TokensOf(template.TextEn));
        Assert.Equal("{labName}: open {url} as {username}", NotificationTemplate.Placeholders.ToCanonical(template.TextEn));
        Assert.Equal("Panic for {patient} ({accession})", NotificationTemplate.Placeholders.ToCanonical("Panic for {patient} ({acc})"));
    }

    [Fact]
    public void An_unknown_placeholder_is_refused_NTF_003()
    {
        var refusal = Assert.Throws<DomainException>(() => NotificationTemplate.Create("otp", TemplateChannel.Sms, "Code {code} for {customer}", "رمز {code}", null, null));
        Assert.Equal(PlatformAdminCodes.UnknownPlaceholder, refusal.Code);
        Assert.Contains("{customer}", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Code {code")]
    [InlineData("Code code}")]
    [InlineData("Code { code }")]
    public void An_unbalanced_brace_is_refused_NTF_002(string text)
    {
        Assert.Equal(PlatformAdminCodes.TemplateTextInvalid, Assert.Throws<DomainException>(() => NotificationTemplate.Create("otp", TemplateChannel.Sms, text, "رمز", null, null)).Code);
    }

    [Fact]
    public void An_email_template_needs_both_subjects_NTF_004_and_a_code_is_kebab_case_NTF_001()
    {
        Assert.Equal(PlatformAdminCodes.SubjectRequired, Assert.Throws<DomainException>(() => NotificationTemplate.Create("welcome", TemplateChannel.Email, "Hello", "مرحبا", "Welcome", null)).Code);
        Assert.Equal(PlatformAdminCodes.TemplateCodeInvalid, Assert.Throws<DomainException>(() => NotificationTemplate.Create("bad code", TemplateChannel.Sms, "Hello", "مرحبا", null, null)).Code);

        var email = NotificationTemplate.Create("welcome", TemplateChannel.Email, "Hello", "مرحبا", "Welcome", "أهلا");
        email.Update(TemplateChannel.Sms, "Hi {username}", "أهلا {username}", null, null);
        Assert.Equal((TemplateChannel.Sms, "Hi {username}"), (email.Channel, email.TextEn));
    }

    [Fact]
    public void An_analyser_model_keeps_distinct_modes_and_refuses_too_many_LIB_002()
    {
        var model = AnalyserModel.Create(new AnalyserModelData("Roche", "cobas c311", "Chemistry", AnalyserProtocol.Astm, PhysicalLink.Serial, ["Bidirectional", "bidirectional", " Query "], "1.2"));
        Assert.Equal(["Bidirectional", "Query"], model.Modes);

        var tooMany = Enumerable.Range(1, AnalyserModel.MaxModes + 1).Select(index => $"mode-{index}").ToList();
        Assert.Equal(PlatformAdminCodes.ModesInvalid, Assert.Throws<DomainException>(() => AnalyserModel.Create(new AnalyserModelData("V", "M", "D", AnalyserProtocol.Hl7, PhysicalLink.Tcp, tooMany, null))).Code);
    }

    [Fact]
    public void A_pcr_kit_stores_compact_JSON_and_refuses_invalid_JSON_LIB_003()
    {
        var kit = PcrKitTemplate.Create("SARS-CoV-2 RT-PCR", null, "{ \"sheet\": \"Results\", \"valueColumn\": \"Cq\" }", "[{\"gene\":\"N\",\"positive\":38}]", "[]");
        Assert.Equal("{\"sheet\":\"Results\",\"valueColumn\":\"Cq\"}", kit.LayoutJson);

        Assert.Equal(PlatformAdminCodes.JsonInvalid, Assert.Throws<DomainException>(() => PcrKitTemplate.Create("Kit", null, "{ sheet: 1 }", "[]", "[]")).Code);
        Assert.Equal(PlatformAdminCodes.JsonInvalid, Assert.Throws<DomainException>(() => PcrKitTemplate.Create("Kit", null, "42", "[]", "[]")).Code);
    }

    [Fact]
    public void A_pcr_kit_refuses_an_inactive_analyser_model_LIB_012()
    {
        var model = AnalyserModel.Create(new AnalyserModelData("Bio-Rad", "CFX96", "PCR", AnalyserProtocol.Proprietary, PhysicalLink.File, [], null));
        model.Deactivate();
        Assert.Equal(PlatformAdminCodes.AnalyserModelInactive, Assert.Throws<DomainException>(() => PcrKitTemplate.Create("Kit", model, "{}", "[]", "[]")).Code);
    }

    [Fact]
    public void I1_a_code_system_import_upserts_by_code()
    {
        var loinc = CodeSystem.Create(CodeSystemName.Loinc, "2.78", "test identifiers");
        var first = loinc.Import(new Dictionary<string, CodeSystemItem>(), [("2345-7", "Glucose"), ("718-7", "Hemoglobin"), ("2345-7", "Glucose [Mass/volume]")], Now);

        Assert.Equal((2, 0, 0, 2), (first.Inserted, first.Updated, first.Unchanged, loinc.ItemCount));
        Assert.Equal("Glucose [Mass/volume]", first.NewItems.Single(item => item.Code == "2345-7").Display);

        var existing = first.NewItems.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var second = loinc.Import(existing, [("2345-7", "Glucose [Mass/volume]"), ("718-7", "Hemoglobin [Mass/volume] in Blood"), ("4548-4", "HbA1c")], Now.AddDays(1));

        Assert.Equal((1, 1, 1, 3), (second.Inserted, second.Updated, second.Unchanged, loinc.ItemCount));
        Assert.Equal("Hemoglobin [Mass/volume] in Blood", existing["718-7"].Display);
        Assert.Equal(Now.AddDays(1), loinc.LastImportedAtUtc);
        Assert.Equal(2, loinc.DomainEvents.OfType<CodeSystemImported>().Count());
    }

    [Fact]
    public void I1_a_code_with_spaces_or_an_empty_display_is_refused_LIB_004()
    {
        var cpt = CodeSystem.Create(CodeSystemName.Cpt, "2026", null);
        Assert.Equal(PlatformAdminCodes.CodeSystemItemInvalid, Assert.Throws<DomainException>(() => cpt.Import(new Dictionary<string, CodeSystemItem>(), [("85 025", "CBC")], Now)).Code);
        Assert.Equal(PlatformAdminCodes.CodeSystemItemInvalid, Assert.Throws<DomainException>(() => cpt.Import(new Dictionary<string, CodeSystemItem>(), [("85025", " ")], Now)).Code);
    }

    [Fact]
    public void A_rule_template_needs_a_name_and_a_formula()
    {
        var rule = RuleTemplate.Create("Delta glucose", "Chemistry", "abs(R - LR) > 0.2 * LR", null);
        Assert.True(rule.IsActive);
        Assert.Equal(PlatformAdminCodes.LibraryTextInvalid, Assert.Throws<DomainException>(() => RuleTemplate.Create("x", null, " ", null)).Code);
    }

    [Fact]
    public void S1_to_S3_the_wizard_request_moves_Pending_Verified_Completed()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var request = SubscriptionRequest.Start(Profile(egypt, cairo), egypt, cairo, "ar-EG", Now);
        Assert.Equal((SubscriptionRequestStatus.Pending, "ar", Now.AddHours(1), 1), (request.Status, request.Language, request.ExpiresAtUtc, request.CodesSent));

        var early = Assert.Throws<InvalidStateTransitionException>(() => request.EnsureCanComplete(Now));
        Assert.Equal(PlatformAdminCodes.SubscriptionNotVerified, early.Code);

        request.MarkVerified(Now.AddMinutes(2));
        var tenantId = Guid.CreateVersion7();
        request.Complete(tenantId, Now.AddMinutes(5));

        Assert.Equal((SubscriptionRequestStatus.Completed, tenantId), (request.Status, request.TenantId));
        Assert.Equal(PlatformAdminCodes.SubscriptionNotVerified, Assert.Throws<InvalidStateTransitionException>(() => request.Complete(tenantId, Now.AddMinutes(6))).Code);
        Assert.Equal(PlatformAdminCodes.SubscriptionNotPending, Assert.Throws<InvalidStateTransitionException>(() => request.MarkVerified(Now.AddMinutes(6))).Code);
    }

    [Fact]
    public void S1_an_expired_request_refuses_every_step_SUB_001()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var request = SubscriptionRequest.Start(Profile(egypt, cairo), egypt, cairo, null, Now);
        var late = Now.AddHours(1);

        Assert.Equal(PlatformAdminCodes.SubscriptionExpired, Assert.Throws<DomainException>(() => request.EnsureCanVerify(late)).Code);
        Assert.Equal(PlatformAdminCodes.SubscriptionExpired, Assert.Throws<DomainException>(() => request.RecordCodeSent(late)).Code);
    }

    [Fact]
    public void S1_at_most_five_codes_are_sent_SUB_004()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var request = SubscriptionRequest.Start(Profile(egypt, cairo), egypt, cairo, "en", Now);
        for (var i = 1; i < SubscriptionRequest.MaxCodesSent; i++)
        {
            request.RecordCodeSent(Now.AddMinutes(i));
        }

        Assert.Equal(SubscriptionRequest.MaxCodesSent, request.CodesSent);
        Assert.Equal(PlatformAdminCodes.TooManyCodes, Assert.Throws<DomainException>(() => request.RecordCodeSent(Now.AddMinutes(10))).Code);
    }
}
