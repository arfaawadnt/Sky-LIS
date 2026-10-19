using Platform.Domain.Pricing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Pricing;

/// <summary>
/// Office contacts of a payer (CR 2026-10-19 C19, intake LIS-INT-PRC-001 §19 P1/P2): phones and WhatsApp cleaned and
/// validated (PRC-017), the e-mail of basic shape (PRC-018), every value optional, and an edit of the contacts alone
/// recorded as a <see cref="PayerUpdated"/>.
/// </summary>
public sealed class PayerContactsTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly LocalizedText Axa = new("AXA Insurance", "أكسا للتأمين");

    private static Payer Create(PayerContacts contacts) => Payer.Create(Axa, "axa", PayerType.Insurance, false, null, contacts, Now);

    [Fact]
    public void P1_Create_cleans_the_contacts_and_blanks_become_null()
    {
        var payer = Create(new PayerContacts("  +20 (2)   2735-1234 ", "   ", "01001234567", " contracts@axa.example.com "));

        Assert.Equal("+20 (2) 2735-1234", payer.Phone1);
        Assert.Null(payer.Phone2);
        Assert.Equal("01001234567", payer.WhatsApp);
        Assert.Equal("contracts@axa.example.com", payer.Email);
        Assert.Equal(new PayerContacts("+20 (2) 2735-1234", null, "01001234567", "contracts@axa.example.com"), payer.Contacts);
    }

    [Fact]
    public void P1_The_legacy_create_leaves_every_contact_empty()
    {
        var payer = Payer.Create(Axa, "axa", PayerType.Insurance, false, null, Now);

        Assert.Equal(PayerContacts.None, payer.Contacts);
    }

    [Theory]
    [InlineData("0100 ABC 1234")]                           // letters
    [InlineData("+ - ( )")]                                 // no digit
    [InlineData("0100.123.4567")]                           // a dot
    [InlineData("123456789012345678901234567890123")]       // 33 characters
    public void P1_An_invalid_phone_or_WhatsApp_number_is_refused_PRC_017(string number)
    {
        foreach (var contacts in new[] { new PayerContacts(number, null, null, null), new PayerContacts(null, number, null, null), new PayerContacts(null, null, number, null) })
        {
            var refusal = Assert.Throws<DomainException>(() => Create(contacts));

            Assert.Equal(PricingCodes.ContactNumberInvalid, refusal.Code);
            Assert.Equal(DomainFault.Validation, refusal.Fault);
        }
    }

    [Fact]
    public void P1_A_number_of_32_characters_with_every_allowed_symbol_is_accepted()
    {
        var payer = Create(new PayerContacts("+20 (2) 1234-5678 (9) 0123-456 7", null, null, null));

        Assert.Equal(32, payer.Phone1!.Length);
    }

    [Theory]
    [InlineData("contracts.axa.example.com")]      // no @
    [InlineData("a@b@c.com")]                      // two @
    [InlineData("@axa.com")]                       // nothing before @
    [InlineData("contracts@axa")]                  // domain without a dot
    [InlineData("contracts@.axa.com")]             // domain starting with a dot
    [InlineData("contracts@axa.com.")]             // domain ending with a dot
    [InlineData("con tracts@axa.com")]             // a space
    public void P1_An_e_mail_without_the_basic_shape_is_refused_PRC_018(string email)
    {
        var refusal = Assert.Throws<DomainException>(() => Create(new PayerContacts(null, null, null, email)));

        Assert.Equal(PricingCodes.EmailInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void P1_An_e_mail_longer_than_254_characters_is_refused_PRC_018()
    {
        var local = new string('a', 254 - "@axa.com".Length);
        Assert.Equal(254, Create(new PayerContacts(null, null, null, local + "@axa.com")).Email!.Length);

        var refusal = Assert.Throws<DomainException>(() => Create(new PayerContacts(null, null, null, local + "a@axa.com")));
        Assert.Equal(PricingCodes.EmailInvalid, refusal.Code);
    }

    [Fact]
    public void P2_Update_of_the_contacts_alone_raises_PayerUpdated_and_the_same_contacts_raise_nothing()
    {
        var payer = Create(new PayerContacts("01001234567", null, null, "a@axa.com"));
        payer.ClearDomainEvents();

        payer.Update(Axa, "axa", PayerType.Insurance, false, null, new PayerContacts("0100 123 4567", "0222", "01001234567", "a@axa.com"), Now);

        Assert.Equal(new PayerContacts("0100 123 4567", "0222", "01001234567", "a@axa.com"), payer.Contacts);
        Assert.IsType<PayerUpdated>(Assert.Single(payer.DomainEvents));

        payer.ClearDomainEvents();
        payer.Update(Axa, "axa", PayerType.Insurance, false, null, new PayerContacts("  0100   123 4567 ", "0222", "01001234567", " a@axa.com "), Now);
        Assert.Empty(payer.DomainEvents);
    }

    [Fact]
    public void P2_The_legacy_update_keeps_the_contacts()
    {
        var payer = Create(new PayerContacts("01001234567", null, null, "a@axa.com"));

        payer.Update(new LocalizedText("AXA", "أكسا"), "axa", PayerType.Insurance, false, null, Now);

        Assert.Equal(new PayerContacts("01001234567", null, null, "a@axa.com"), payer.Contacts);
    }

    [Fact]
    public void P2_An_invalid_contact_on_update_is_refused_and_nothing_changes()
    {
        var payer = Create(new PayerContacts("01001234567", null, null, "a@axa.com"));
        payer.ClearDomainEvents();

        var refusal = Assert.Throws<DomainException>(() => payer.Update(new LocalizedText("AXA", "أكسا"), "axa-eg", PayerType.Credit, false, null, new PayerContacts("01001234567", null, null, "not-an-email"), Now));

        Assert.Equal(PricingCodes.EmailInvalid, refusal.Code);
        Assert.Equal(("AXA Insurance", "AXA", PayerType.Insurance, "a@axa.com"), (payer.Name.En, payer.SearchKey, payer.Type, payer.Email));
        Assert.Empty(payer.DomainEvents);
    }
}
