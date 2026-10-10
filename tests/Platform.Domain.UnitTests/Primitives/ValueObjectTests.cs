using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Primitives;

public sealed class ValueObjectTests
{
    [Fact]
    public void Value_objects_compare_by_content()
    {
        Assert.Equal(new Money(10m, "EGP"), new Money(10m, "EGP"));
        Assert.True(new Money(10m, "EGP") == new Money(10m, "EGP"));
        Assert.Equal(new Money(10m, "EGP").GetHashCode(), new Money(10m, "EGP").GetHashCode());
        Assert.NotEqual(new Money(10m, "EGP"), new Money(10m, "USD"));
        Assert.True(new Money(10m, "EGP") != new Money(11m, "EGP"));
        Assert.False(new Money(10m, "EGP").Equals((object?)null));
    }

    [Fact]
    public void An_approval_needs_an_actor_and_trims_its_comment()
    {
        var approval = new Approval(Guid.CreateVersion7(), FixedClock.Instant, "  fine ");

        Assert.Equal("fine", approval.Comment);
        Assert.Null(new Approval(Guid.CreateVersion7(), FixedClock.Instant, "   ").Comment);
        Assert.Throws<ArgumentException>(() => new Approval(Guid.Empty, FixedClock.Instant));
        Assert.Throws<ArgumentException>(() => new Approval(Guid.CreateVersion7(), FixedClock.Instant, new string('x', Approval.MaxCommentLength + 1)));
    }

    [Fact]
    public void Localized_text_requires_both_languages_and_resolves_by_tag()
    {
        var text = new LocalizedText(" Glucose ", "جلوكوز");

        Assert.Equal("Glucose", text.En);
        Assert.Equal("جلوكوز", text.Ar);
        Assert.Equal("جلوكوز", text.For("ar-EG"));
        Assert.Equal("Glucose", text.For("en"));
        Assert.Equal("Glucose", text.For(null));
        Assert.Equal(new LocalizedText("Glucose", "جلوكوز"), text);
        Assert.Throws<ArgumentException>(() => new LocalizedText("Glucose", " "));
        Assert.Throws<ArgumentException>(() => new LocalizedText(new string('x', LocalizedText.MaxLength + 1), "x"));
    }
}
