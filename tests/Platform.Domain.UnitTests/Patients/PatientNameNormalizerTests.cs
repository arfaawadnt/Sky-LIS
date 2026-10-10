using Platform.Domain.Patients;

namespace Platform.Domain.UnitTests.Patients;

/// <summary>The name keys and the similarity rule of BR-01 / REG-002 (intake §13, scenario S-SIM).</summary>
public sealed class PatientNameNormalizerTests
{
    [Theory]
    [InlineData("  Mohamed   Ahmed  ", "mohamed ahmed")]
    [InlineData("Mr. Mohamed Ahmed", "mohamed ahmed")]
    [InlineData("Dr Mohamed-Ahmed", "mohamed ahmed")]
    [InlineData("José Müller", "jose muller")]
    [InlineData("Baby of Mona", "of mona")]
    [InlineData("Abdel Rahman Ali", "abdelrahman ali")]
    [InlineData("Abd El Rahman Ali", "abdelrahman ali")]
    [InlineData("Mohamed El Sayed", "mohamed elsayed")]
    [InlineData("O'Neil", "o neil")]
    public void Latin_names_are_folded_to_comparable_tokens(string name, string key) => Assert.Equal(key, PatientNameNormalizer.Normalize(name));

    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إيمان", "ايمان")]
    [InlineData("آمال", "امال")]
    [InlineData("مُحَمَّد", "محمد")]
    [InlineData("محـــمد", "محمد")]
    [InlineData("فاطمة", "فاطمه")]
    [InlineData("مصطفى", "مصطفي")]
    [InlineData("هانئ", "هاني")]
    [InlineData("مؤمن", "مومن")]
    [InlineData("عبد الله", "عبدالله")]
    [InlineData("عبدالله", "عبدالله")]
    [InlineData("أبو بكر", "ابوبكر")]
    [InlineData("السيد محمد", "محمد")]
    [InlineData("د. منى", "مني")]
    [InlineData("الدكتورة منى", "مني")]
    [InlineData("أ.د. سامي", "سامي")]
    [InlineData("سيد علي", "سيد علي")]
    public void Arabic_names_unify_letter_forms_strip_marks_and_titles_and_join_compounds(string name, string key) =>
        Assert.Equal(key, PatientNameNormalizer.Normalize(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mr.")]
    public void Empty_or_title_only_names_have_no_key(string? name)
    {
        Assert.Equal(string.Empty, PatientNameNormalizer.Normalize(name));
        Assert.False(PatientNameNormalizer.IsSimilar(name, name));
    }

    [Theory]
    [InlineData("Mohamed Ahmed", "mohamed  ahmed")]
    [InlineData("Mohamed Ahmed", "Ahmed Mohamed")]
    [InlineData("Mohamed Ahmed", "Mohamed Ahmed Ali")]
    [InlineData("Ahmed Mohamed Ali Hassan", "Mohamed Hassan")]
    [InlineData("Ahmed", "Mr Ahmed")]
    [InlineData("محمد أحمد", "مُحمّد احمد علي")]
    [InlineData("عبد الله فاطمة", "عبدالله فاطمه")]
    [InlineData("Abdel Rahman Ali", "Abd El Rahman Ali")]
    public void Similar_names_match(string left, string right)
    {
        Assert.True(PatientNameNormalizer.IsSimilar(left, right));
        Assert.True(PatientNameNormalizer.IsSimilar(right, left));
    }

    [Theory]
    [InlineData("Ahmed", "Ahmed Ali")]
    [InlineData("Mohamed Ahmed", "Mohamed Ali")]
    [InlineData("Mohamed Ahmed Ali", "Mohamed Ahmed Hassan")]
    [InlineData("Mona Hassan", "منى حسن")]
    [InlineData("Ali", "Alia")]
    public void Different_names_do_not_match(string left, string right) => Assert.False(PatientNameNormalizer.IsSimilar(left, right));

    [Fact]
    public void KeysMatch_compares_stored_keys_without_renormalising()
    {
        Assert.True(PatientNameNormalizer.KeysMatch("mohamed ahmed ali", "ali mohamed"));
        Assert.False(PatientNameNormalizer.KeysMatch("mohamed", "mohamed ahmed"));
        Assert.False(PatientNameNormalizer.KeysMatch(string.Empty, "mohamed"));
        Assert.False(PatientNameNormalizer.KeysMatch(null, null));
    }
}
