using Platform.Domain.Organization;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Organization;

/// <summary>Acceptance scenarios AS-B1…AS-B3 of intake LIS-INT-ORG-001 §16 (branch transitions B1–B4).</summary>
public sealed class BranchTests
{
    private static readonly LocalizedText Name = new("Nasr City", "مدينة نصر");
    private static readonly Guid Country = Guid.CreateVersion7();
    private static readonly Guid Governorate = Guid.CreateVersion7();

    private static Branch NewBranch(string code = " nasr-1 ") =>
        Branch.Create(code, Name, Country, Governorate, "+20 2 2222 3333", " Nasr@Lab.Example ", "egp", FixedClock.Instant);

    [Fact]
    public void AS_B1_Create_normalises_code_contact_and_currency_and_raises_BranchCreated()
    {
        var branch = NewBranch();

        Assert.Equal("NASR-1", branch.Code);
        Assert.Equal("nasr@lab.example", branch.Email);
        Assert.Equal("+20 2 2222 3333", branch.Phone);
        Assert.Equal("EGP", branch.Currency);
        Assert.Equal((Country, Governorate), (branch.CountryId, branch.GovernorateId));
        Assert.True(branch.IsActive);
        var created = Assert.IsType<BranchCreated>(Assert.Single(branch.DomainEvents));
        Assert.Equal((branch.Id, "NASR-1"), (created.BranchId, created.Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("SEVENTEEN-CHARS-X")]
    public void AS_B1_An_invalid_code_is_ORG_003(string code)
    {
        var refusal = Assert.Throws<DomainException>(() => NewBranch(code));

        Assert.Equal(OrganizationCodes.BranchCodeInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void AS_B3_A_governorate_without_its_country_is_ORG_008()
    {
        var refusal = Assert.Throws<DomainException>(() => Branch.Create("B1", Name, null, Governorate, null, null, "EGP", FixedClock.Instant));

        Assert.Equal(OrganizationCodes.BranchContactInvalid, refusal.Code);
    }

    [Theory]
    [InlineData("not-an-email", "+20 100 000 0000")]
    [InlineData("a@b.c", "phone!")]
    public void AS_B3_Malformed_contact_data_is_ORG_008(string email, string phone)
    {
        var refusal = Assert.Throws<DomainException>(() => Branch.Create("B1", Name, null, null, phone, email, "EGP", FixedClock.Instant));

        Assert.Equal(OrganizationCodes.BranchContactInvalid, refusal.Code);
    }

    [Theory]
    [InlineData("EG")]
    [InlineData("E1P")]
    [InlineData("")]
    public void AS_B3_A_currency_that_is_not_three_letters_is_ORG_009(string currency)
    {
        var refusal = Assert.Throws<DomainException>(() => Branch.Create("B1", Name, null, null, null, null, currency, FixedClock.Instant));

        Assert.Equal(OrganizationCodes.CurrencyInvalid, refusal.Code);
    }

    [Fact]
    public void B2_Update_changes_everything_but_the_code_and_clears_empty_contact_data()
    {
        var branch = NewBranch();

        branch.Update(new LocalizedText("Maadi", "المعادي"), null, null, " ", null, "usd");

        Assert.Equal("NASR-1", branch.Code);
        Assert.Equal("Maadi", branch.Name.En);
        Assert.Null(branch.CountryId);
        Assert.Null(branch.Phone);
        Assert.Null(branch.Email);
        Assert.Equal("USD", branch.Currency);
    }

    [Fact]
    public void AS_B2_Deactivation_and_reactivation_are_idempotent_with_one_event_each()
    {
        var branch = NewBranch();
        branch.ClearDomainEvents();

        branch.Deactivate(FixedClock.Instant);
        branch.Deactivate(FixedClock.Instant);
        Assert.False(branch.IsActive);
        branch.Reactivate(FixedClock.Instant);
        branch.Reactivate(FixedClock.Instant);

        Assert.True(branch.IsActive);
        Assert.Collection(branch.DomainEvents, first => Assert.IsType<BranchDeactivated>(first), second => Assert.IsType<BranchReactivated>(second));
    }
}
