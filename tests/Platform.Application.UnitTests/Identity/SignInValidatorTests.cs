using Platform.Application.Identity.Commands;

namespace Platform.Application.UnitTests.Identity;

/// <summary>LIS-CR-005: a sign-in names exactly one target — a laboratory address of the right shape, or the Super Admin sign-in.</summary>
public sealed class SignInValidatorTests
{
    private static readonly SignInCommandValidator Validator = new();

    private static SignInCommand Command(string? laboratory, bool? admin) => new(laboratory, admin, "lab.user", "Correct-Horse-Battery-Staple-9", null, null);

    [Theory]
    [InlineData("al-noor", null)]
    [InlineData("al-noor", false)]
    [InlineData(" AL-Noor ", null)]   // trimmed and lower-cased by the handler
    [InlineData(null, true)]
    public void One_target_passes(string? laboratory, bool? admin)
    {
        Assert.True(Validator.Validate(Command(laboratory, admin)).IsValid);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, false)]
    [InlineData("al-noor", true)]
    public void Neither_or_both_targets_are_refused(string? laboratory, bool? admin)
    {
        var result = Validator.Validate(Command(laboratory, admin));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SignInCommand.Laboratory));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("al noor")]
    [InlineData("al_noor")]
    [InlineData("مختبر")]
    [InlineData("")]
    public void A_malformed_address_is_refused_as_shape(string laboratory)
    {
        var result = Validator.Validate(Command(laboratory, null));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SignInCommand.Laboratory));
        Assert.False(Validator.Validate(Command(new string('a', 41), null)).IsValid);
    }

    [Fact]
    public void The_target_is_administration_only_with_admin_true_and_no_laboratory()
    {
        Assert.True(Command(null, true).AtAdministration);
        Assert.False(Command("al-noor", null).AtAdministration);
        Assert.False(Command("al-noor", true).AtAdministration);
    }
}
