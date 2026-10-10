using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>Intake LIS-INT-PLT-001 §7 P1–P5 and INV-PLT-001…004: one test per transition, refusal and invariant.</summary>
public sealed class TenantTests
{
    [Fact]
    public void P1_manual_provisioning_is_Active_with_normalised_contacts()
    {
        var client = Client(TenantSource.Manual);

        Assert.Equal(TenantStatus.Active, client.Status);
        Assert.Equal(TenantSource.Manual, client.Source);
        Assert.Equal(Now, client.ActivatedAtUtc);
        Assert.Null(client.TrialEndsAtUtc);
        Assert.Equal("+201012345678", client.Mobile);
        Assert.Equal("+201112223333", client.ManagerMobile);
        Assert.Equal("info@alnoor.example", client.Email);
        var raised = Assert.IsType<TenantProvisioned>(Assert.Single(client.DomainEvents));
        Assert.Equal((client.Id, "al-noor", "Manual"), (raised.TenantId, raised.PortalSlug, raised.Source));
    }

    [Fact]
    public void P1_subscription_starts_a_trial_of_the_configured_days()
    {
        var client = Client(TenantSource.Subscription, trialDays: 14);

        Assert.Equal(TenantStatus.Trial, client.Status);
        Assert.Equal(Now.AddDays(14), client.TrialEndsAtUtc);
        Assert.True(client.IsUsableAt(Now.AddDays(13)));
        Assert.False(client.IsUsableAt(Now.AddDays(14)));
    }

    [Fact]
    public void P1_subscription_without_trial_days_is_Active_at_once()
    {
        Assert.Equal(TenantStatus.Active, Client(TenantSource.Subscription, trialDays: 0).Status);
    }

    [Fact]
    public void P1_refused_when_the_governorate_is_of_another_country_GEO_013()
    {
        var egypt = Egypt();
        var kuwait = Kuwait();
        var cairo = Cairo(egypt);
        var profile = ClientProfile.Create("Lab", "مختبر", kuwait, cairo.Id, "Street", "51234567", null, "Manager", "51234568", null);

        var refusal = Assert.Throws<DomainException>(() => Tenant.Provision(profile, kuwait, cairo, Starter(), "kuwait-lab", TenantSource.Manual, "ar", 14, Now));
        Assert.Equal(PlatformAdminCodes.GovernorateNotInCountry, refusal.Code);
    }

    [Fact]
    public void P1_refused_on_an_inactive_plan_PLN_010()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var plan = Starter();
        plan.Deactivate();

        var refusal = Assert.Throws<DomainException>(() => Tenant.Provision(Profile(egypt, cairo), egypt, cairo, plan, "al-noor", TenantSource.Manual, "en", 14, Now));
        Assert.Equal(PlatformAdminCodes.PlanInactive, refusal.Code);
    }

    [Fact]
    public void P1_refused_on_an_inactive_governorate_GEO_012()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        cairo.Deactivate();

        var refusal = Assert.Throws<DomainException>(() => Tenant.Provision(Profile(egypt, cairo), egypt, cairo, Starter(), "al-noor", TenantSource.Manual, "en", 14, Now));
        Assert.Equal(PlatformAdminCodes.GovernorateInactive, refusal.Code);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("-lab")]
    [InlineData("lab-")]
    [InlineData("al--noor")]
    [InlineData("admin")]
    [InlineData("al noor")]
    [InlineData("مختبر")]
    public void INV_PLT_001_invalid_or_reserved_slugs_are_refused_CLI_001(string slug)
    {
        var refusal = Assert.Throws<DomainException>(() => Tenant.CleanSlug(slug));
        Assert.Equal(PlatformAdminCodes.PortalSlugInvalid, refusal.Code);
    }

    [Theory]
    [InlineData("Al Noor Lab", "seed", "al-noor-lab")]
    [InlineData("  MedLine (Giza) ", "seed", "medline-giza")]
    [InlineData("معمل النور", "u.manager01", "lab-anager01")]
    [InlineData("Admin", "x", "lab-00x")]
    public void A_slug_is_suggested_from_the_English_name(string name, string seed, string expected)
    {
        var slug = Tenant.SuggestSlug(name, seed);
        Assert.Equal(expected, slug);
        Assert.Equal(slug, Tenant.CleanSlug(slug));
    }

    [Fact]
    public void The_portal_url_uses_the_configured_domain()
    {
        var client = Client();
        Assert.Equal("https://al-noor.skylis.app", client.PortalUrl(""));
        Assert.Equal("https://al-noor.lis.example", client.PortalUrl(".lis.example."));
    }

    [Fact]
    public void P2_activate_ends_a_trial_and_refuses_a_second_time_CLI_010()
    {
        var client = Client(TenantSource.Subscription);
        client.Activate(Now.AddDays(3));

        Assert.Equal(TenantStatus.Active, client.Status);
        Assert.Null(client.TrialEndsAtUtc);
        Assert.Contains(client.DomainEvents, raised => raised is TenantActivated);
        var refusal = Assert.Throws<InvalidStateTransitionException>(() => client.Activate(Now.AddDays(4)));
        Assert.Equal(PlatformAdminCodes.ClientAlreadyActive, refusal.Code);
    }

    [Fact]
    public void P3_deactivate_needs_a_reason_CLI_021_and_blocks_the_client()
    {
        var client = Client();
        var refusal = Assert.Throws<DomainException>(() => client.Deactivate("  ", Now));
        Assert.Equal(PlatformAdminCodes.DeactivationReasonRequired, refusal.Code);

        client.Deactivate(" unpaid ", Now);
        Assert.Equal(TenantStatus.Inactive, client.Status);
        Assert.Equal("unpaid", client.DeactivationReason);
        Assert.False(client.IsUsableAt(Now));
        var raised = Assert.IsType<TenantDeactivated>(client.DomainEvents.Last());
        Assert.Equal("unpaid", raised.Reason);
    }

    [Fact]
    public void P3_deactivating_twice_is_refused_CLI_011_and_reactivation_clears_the_reason()
    {
        var client = Client();
        client.Deactivate("unpaid", Now);
        var refusal = Assert.Throws<InvalidStateTransitionException>(() => client.Deactivate("again", Now));
        Assert.Equal(PlatformAdminCodes.ClientAlreadyInactive, refusal.Code);

        client.Activate(Now.AddDays(1));
        Assert.Equal(TenantStatus.Active, client.Status);
        Assert.Null(client.DeactivationReason);
        Assert.True(client.IsUsableAt(Now.AddDays(1)));
    }

    [Fact]
    public void P5_change_plan_raises_once_and_refuses_an_inactive_plan()
    {
        var client = Client();
        var standard = Plan.Create("STANDARD", new PlanTerms(new SharedKernel.Localization.LocalizedText("Standard", "القياسية"), 3500m, "EGP", 3, 20, 2000, 5));
        client.ChangePlan(standard, Now);
        client.ChangePlan(standard, Now);   // idempotent

        Assert.Equal(standard.Id, client.PlanId);
        Assert.Single(client.DomainEvents.OfType<TenantPlanChanged>());

        var retired = Plan.Create("RETIRED", new PlanTerms(new SharedKernel.Localization.LocalizedText("Old", "قديمة"), 1m, "EGP", 1, 1, 0, 0));
        retired.Deactivate();
        Assert.Equal(PlatformAdminCodes.PlanInactive, Assert.Throws<DomainException>(() => client.ChangePlan(retired, Now)).Code);
    }

    [Fact]
    public void P4_update_profile_rechecks_the_place()
    {
        var client = Client();
        var egypt = Egypt();
        var giza = Governorate.Create(egypt, "Giza", "الجيزة", null, null);
        var profile = ClientProfile.Create("Al Noor Lab 2", "معمل النور ٢", egypt, giza.Id, "1 Pyramids Rd.", "+201098765432", null, "Dr. Mona", "01112223333", null);

        client.UpdateProfile(profile, egypt, giza, "ar");

        Assert.Equal(("Al Noor Lab 2", giza.Id, "ar"), (client.Name.En, client.GovernorateId, client.PreferredLanguage));
        Assert.Null(client.Email);
    }

    [Fact]
    public void An_administrator_is_attached_once_provisioned()
    {
        var client = Client();
        var userId = Guid.CreateVersion7();
        client.AttachAdministrator(userId, "noor.admin");
        Assert.Equal((userId, "noor.admin"), (client.AdministratorUserId, client.AdministratorUsername));
        Assert.Equal(PlatformAdminCodes.AdministratorMissing, Assert.Throws<DomainException>(() => client.AttachAdministrator(Guid.Empty, "x")).Code);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two@@at.example")]
    [InlineData("space @x.example")]
    public void A_malformed_email_is_refused_CLI_003(string email)
    {
        var egypt = Egypt();
        var refusal = Assert.Throws<DomainException>(() => ClientProfile.Create("Lab", "مختبر", egypt, Guid.CreateVersion7(), "Street", "01012345678", null, "Manager", "01012345679", email));
        Assert.Equal(PlatformAdminCodes.EmailInvalid, refusal.Code);
    }
}
