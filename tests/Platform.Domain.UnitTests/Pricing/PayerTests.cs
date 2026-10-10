using Platform.Domain.Pricing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Pricing;

/// <summary>Payer and tier scenarios of intake LIS-INT-PRC-001 §16 (P1–P8, INV-PRC-001…004).</summary>
public sealed class PayerTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static Payer Axa() => Payer.Create(new LocalizedText("AXA Insurance", "أكسا للتأمين"), " axa ", PayerType.Insurance, true, " AXA.Portal ", Now);

    [Fact]
    public void S_P1_ok_Create_cleans_the_keys_and_raises_PayerCreated()
    {
        var payer = Axa();

        Assert.Equal(("AXA", PayerType.Insurance, "axa.portal"), (payer.SearchKey, payer.Type, payer.WebUsername));
        Assert.True(payer.IsActive);
        Assert.False(payer.IsDefault);
        Assert.Empty(payer.Tiers);
        var raised = Assert.IsType<PayerCreated>(Assert.Single(payer.DomainEvents));
        Assert.Equal((payer.Id, "AXA", false), (raised.PayerId, raised.SearchKey, raised.IsDefault));
    }

    [Fact]
    public void S_P1_web_Portal_without_username_is_refused_PRC_003()
    {
        var refusal = Assert.Throws<DomainException>(() => Payer.Create(new LocalizedText("Syndicate", "نقابة"), "SYN", PayerType.Credit, true, "  ", Now));

        Assert.Equal(PricingCodes.WebUsernameRequired, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("ÄÖÜ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void P1_Invalid_search_key_is_refused_PRC_002(string key)
    {
        var refusal = Assert.Throws<DomainException>(() => Payer.Create(new LocalizedText("X", "س"), key, PayerType.Credit, false, null, Now));

        Assert.Equal(PricingCodes.SearchKeyInvalid, refusal.Code);
    }

    [Fact]
    public void P1_Invalid_web_username_is_refused_PRC_004()
    {
        var refusal = Assert.Throws<DomainException>(() => Payer.Create(new LocalizedText("X", "س"), "X", PayerType.Credit, true, "bad user!", Now));

        Assert.Equal(PricingCodes.WebUsernameInvalid, refusal.Code);
    }

    [Fact]
    public void P2_Update_changes_details_and_clears_the_username_when_the_portal_is_off()
    {
        var payer = Axa();

        payer.Update(new LocalizedText("AXA", "أكسا"), "axa-eg", PayerType.Credit, false, null);

        Assert.Equal(("AXA-EG", PayerType.Credit, false, (string?)null), (payer.SearchKey, payer.Type, payer.WebPortalEnabled, payer.WebUsername));
        Assert.Equal("أكسا", payer.Name.Ar);
    }

    [Fact]
    public void S_P8_Default_cash_payer_has_a_default_tier_and_is_locked_PRC_020_PRC_021()
    {
        var cash = Payer.CreateDefaultCash(Payer.DefaultSearchKey, Now);

        Assert.True(cash.IsDefault);
        Assert.Equal(PayerType.Cash, cash.Type);
        var tier = Assert.Single(cash.Tiers);
        Assert.True(tier.IsDefault);
        Assert.Equal(cash.Id, tier.PayerId);
        Assert.Equal(2, cash.DomainEvents.Count);
        Assert.IsType<ContractTierAdded>(cash.DomainEvents.Last());

        Assert.Equal(PricingCodes.DefaultPayerLocked, Assert.Throws<DomainException>(() => cash.Deactivate(Now)).Code);
        Assert.Equal(PricingCodes.DefaultPayerLocked, Assert.Throws<DomainException>(() => cash.Update(new LocalizedText("Cash", "نقدي"), "CASH", PayerType.Credit, false, null)).Code);
        Assert.Equal(PricingCodes.DefaultTierLocked, Assert.Throws<DomainException>(() => cash.DeactivateTier(tier.Id, Now)).Code);
        Assert.True(cash.IsActive);
        Assert.True(tier.IsActive);

        cash.Update(new LocalizedText("Cash patients", "مرضى نقدي"), "CASH", PayerType.Cash, false, null);
        Assert.Equal("Cash patients", cash.Name.En);
    }

    [Fact]
    public void P3_P4_Deactivate_and_reactivate_are_idempotent()
    {
        var payer = Axa();
        payer.ClearDomainEvents();

        payer.Deactivate(Now);
        payer.Deactivate(Now);
        Assert.False(payer.IsActive);
        Assert.IsType<PayerDeactivated>(Assert.Single(payer.DomainEvents));

        payer.Reactivate(Now);
        payer.Reactivate(Now);
        Assert.True(payer.IsActive);
        Assert.Equal(2, payer.DomainEvents.Count);
    }

    [Fact]
    public void P5_Add_tier_and_S_P5_dup_same_name_refused_PRC_103()
    {
        var payer = Axa();
        payer.ClearDomainEvents();

        var gold = payer.AddTier(new LocalizedText("Corporate Gold", "الشركات الذهبي"), Now);

        Assert.Equal(payer.Id, gold.PayerId);
        Assert.True(gold.IsActive);
        Assert.False(gold.IsDefault);
        var raised = Assert.IsType<ContractTierAdded>(Assert.Single(payer.DomainEvents));
        Assert.Equal(gold.Id, raised.ContractTierId);

        var refusal = Assert.Throws<DomainException>(() => payer.AddTier(new LocalizedText("corporate gold", "أخرى"), Now));
        Assert.Equal(PricingCodes.TierNameExists, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
        Assert.Single(payer.Tiers);
    }

    [Fact]
    public void P6_Rename_tier_keeps_names_unique_and_unknown_tier_is_PRC_405()
    {
        var payer = Axa();
        var gold = payer.AddTier(new LocalizedText("Gold", "ذهبي"), Now);
        var silver = payer.AddTier(new LocalizedText("Silver", "فضي"), Now);

        payer.RenameTier(gold.Id, new LocalizedText("Gold plus", "ذهبي بلس"));
        Assert.Equal("Gold plus", gold.Name.En);
        payer.RenameTier(silver.Id, new LocalizedText("Silver", "فضي معدل"));   // its own name is not a clash

        Assert.Equal(PricingCodes.TierNameExists, Assert.Throws<DomainException>(() => payer.RenameTier(silver.Id, new LocalizedText("Gold plus", "x"))).Code);
        var missing = Assert.Throws<DomainException>(() => payer.RenameTier(Guid.CreateVersion7(), new LocalizedText("Y", "ي")));
        Assert.Equal((PricingCodes.TierNotFound, DomainFault.NotFound), (missing.Code, missing.Fault));
    }

    [Fact]
    public void P7_Deactivate_and_reactivate_tier_raise_once()
    {
        var payer = Axa();
        var gold = payer.AddTier(new LocalizedText("Gold", "ذهبي"), Now);
        payer.ClearDomainEvents();

        payer.DeactivateTier(gold.Id, Now);
        payer.DeactivateTier(gold.Id, Now);
        Assert.False(gold.IsActive);
        payer.ReactivateTier(gold.Id, Now);

        Assert.Collection(
            payer.DomainEvents,
            first => Assert.IsType<ContractTierDeactivated>(first),
            second => Assert.IsType<ContractTierReactivated>(second));
    }
}
