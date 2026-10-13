using Platform.Application.Patients;
using Platform.Application.Patients.Queries;
using Platform.Domain.Patients;

namespace Platform.Application.UnitTests.Patients;

/// <summary>The read-side rules of intake LIS-INT-PAT-001 §13–§14: search-box parsing, similar classification (S-SIM), VIP masking, CSV safety.</summary>
public sealed class PatientQueryRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    private static PatientRow Row(string fullName, string? nameAr, string? mobile, bool vip = false, DateOnly? birth = null, int? ageYears = null) => new(
        Guid.CreateVersion7(), "PAT-2026-00001", PatientTitle.Mr, fullName, nameAr, PatientNameNormalizer.Normalize(fullName), PatientNameNormalizer.Normalize(nameAr),
        PatientGender.Male, birth, ageYears, ageYears is null ? null : 0, ageYears is null ? null : 0, ageYears is null ? null : Today, Today, null, mobile, null, null, null, null, null, null, null,
        vip, PatientStatus.Active, null, null, null, null, null, DateTimeOffset.UnixEpoch, "Clerk", null, null, 7);

    [Fact]
    public void Search_terms_recognise_numbers_mobiles_national_ids_and_name_tokens()
    {
        Assert.Null(PatientSearchTerms.Parse("   "));

        var number = PatientSearchTerms.Parse("pat-2026-0001")!;
        Assert.Equal("PAT-2026-0001", number.Number);
        Assert.Null(number.Digits);

        var mobile = PatientSearchTerms.Parse("0100 123")!;
        Assert.Equal("0100123", mobile.Digits);

        var arabicDigits = PatientSearchTerms.Parse("٠١٠٠١٢٣")!;
        Assert.Equal("0100123", arabicDigits.Digits);

        var name = PatientSearchTerms.Parse("moh* ahmed")!;
        Assert.Null(name.Digits);
        Assert.Equal(["moh", "ahmed"], name.NameTokens);

        var arabic = PatientSearchTerms.Parse("أحمد")!;
        Assert.Equal(["احمد"], arabic.NameTokens);

        Assert.Equal("29005010101234", PatientSearchTerms.Parse("2900501 0101234")!.NationalId);
    }

    [Fact]
    public void F08_with_the_laboratory_country_a_mobile_typed_with_the_trunk_0_also_finds_a_file_stored_without_it()
    {
        // Validation v0.5.0 F-08: PAT-2026-00020 stored 1019199912; a search for 01019199912 found nothing.
        Assert.Equal("1019199912", PatientSearchTerms.Parse("010 1919 9912", 10)!.Digits);
        Assert.Equal("1019199912", PatientSearchTerms.Parse("٠١٠١٩١٩٩٩١٢", 10)!.Digits);
        Assert.Equal("01019199912", PatientSearchTerms.Parse("01019199912")!.Digits);
        Assert.Equal("01019199912", PatientSearchTerms.Parse("01019199912", 10)!.Number);   // the number search is unchanged
    }

    [Fact]
    public async Task F08_the_form_settings_carry_the_laboratory_country()
    {
        var withCountry = await new GetPatientFormSettingsHandler(new NoSettings(), new FixedCountry(new LaboratoryCountry("EG", 10))).Handle(new GetPatientFormSettingsQuery(), CancellationToken.None);
        Assert.Equal(("EG", 10), (withCountry.CountryCode, withCountry.CountryMobileLength!.Value));

        var without = await new GetPatientFormSettingsHandler(new NoSettings(), new FixedCountry(null)).Handle(new GetPatientFormSettingsQuery(), CancellationToken.None);
        Assert.Equal((null, null, false, false, true), (without.CountryCode, without.CountryMobileLength, without.MandatoryMobile, without.MandatoryNationalId, without.SimilarNameWarning));
    }

    private sealed class FixedCountry(LaboratoryCountry? country) : ILaboratoryCountry
    {
        public Task<LaboratoryCountry?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(country);
    }

    private sealed class NoSettings : Platform.Application.Abstractions.Settings.ITenantSettings
    {
        public ValueTask<T> GetAsync<T>(Platform.Domain.Settings.SettingDescriptor<T> descriptor, CancellationToken cancellationToken) => ValueTask.FromResult(descriptor.Parse(null));

        public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    [Fact]
    public void S_SIM_Similar_candidates_are_classified_exact_mobile_or_name()
    {
        var key = PatientNameNormalizer.Normalize("mohamed  ahmed");

        Assert.Equal(SimilarMatch.Exact, FindSimilarPatientsHandler.Classify(key, "01001234567", Row("Mohamed Ahmed Ali", null, "01001234567")));
        Assert.Equal(SimilarMatch.Mobile, FindSimilarPatientsHandler.Classify(key, "01001234567", Row("Sara Ali", null, "01001234567")));
        Assert.Equal(SimilarMatch.Name, FindSimilarPatientsHandler.Classify(key, "01001234567", Row("Ahmed Mohamed", null, "01112223333")));
        Assert.Null(FindSimilarPatientsHandler.Classify(key, "01001234567", Row("Mohamed Ali", null, "01112223333")));

        var arabicKey = PatientNameNormalizer.Normalize("محمد احمد");
        Assert.Equal(SimilarMatch.Exact, FindSimilarPatientsHandler.Classify(arabicKey, "01112223333", Row("Mohamed Ahmed", "محمد أحمد", "01112223333")));
        Assert.Null(FindSimilarPatientsHandler.Classify(PatientNameNormalizer.Normalize("Dr. Mohammed"), null, Row("Mohammed Ahmed", null, null)));
    }

    [Fact]
    public void The_VIP_flag_is_masked_without_the_confidential_right_and_the_age_is_computed_for_today()
    {
        var row = Row("Mona Hassan", null, null, vip: true, birth: new DateOnly(1990, 5, 1));

        Assert.Null(PatientProjections.ToListItem(row, Today, includeConfidential: false).IsVip);
        Assert.True(PatientProjections.ToListItem(row, Today, includeConfidential: true).IsVip);
        Assert.Null(PatientProjections.ToDetail(row, Today, includeConfidential: false, null, []).IsVip);

        var item = PatientProjections.ToListItem(row, Today, true);
        Assert.Equal((36, 5, 8), (item.Age.Years, item.Age.Months, item.Age.Days));
        Assert.False(item.DateOfBirthIsEstimated);

        var stated = PatientProjections.ToListItem(Row("Ali", null, null, ageYears: 30), Today.AddYears(2), true);
        Assert.Equal(32, stated.Age.Years);
        Assert.True(stated.DateOfBirthIsEstimated);
        Assert.Equal(new DateOnly(1996, 10, 9), stated.DateOfBirth);
    }

    [Fact]
    public void A03_a_VIP_row_is_listed_without_its_identity_for_a_user_without_the_See_VIP_right()
    {
        var vip = Row("Mona Hassan", "منى حسن", "01112223333", vip: true, birth: new DateOnly(1990, 5, 1)) with { NationalId = "29505050101234" };

        var masked = PatientProjections.ToListItem(vip, Today, includeConfidential: true, canSeeVip: false);
        Assert.True(masked.IdentityMasked);
        Assert.True(masked is { FullName: "", NameAr: null, Mobile: null, NationalId: null });
        Assert.Equal((vip.Number, true, 36), (masked.Number, masked.IsVip!.Value, masked.Age.Years));

        var shown = PatientProjections.ToListItem(vip, Today, includeConfidential: false, canSeeVip: true);
        Assert.Equal(("Mona Hassan", "01112223333", "29505050101234", false), (shown.FullName, shown.Mobile, shown.NationalId, shown.IdentityMasked));

        var ordinary = PatientProjections.ToListItem(Row("Ali", null, "01000000000"), Today, includeConfidential: false, canSeeVip: false);
        Assert.Equal(("Ali", false), (ordinary.FullName, ordinary.IdentityMasked));
    }

    [Fact]
    public void R06_a_similar_VIP_file_is_listed_masked_with_its_match_for_a_user_without_the_See_VIP_right()
    {
        var vip = Row("Mona Hassan", "منى حسن", "01112223333", vip: true, birth: new DateOnly(1990, 5, 1)) with { NationalId = "29505050101234" };

        var masked = PatientProjections.ToSimilar(vip, Today, SimilarMatch.Mobile, canSeeVip: false);
        Assert.True(masked is { FullName: "", NameAr: null, Mobile: null, NationalId: null, IdentityMasked: true, Match: "Mobile" });
        Assert.Equal((vip.Id, vip.Number, 36), (masked.Id, masked.Number, masked.Age.Years));

        var shown = PatientProjections.ToSimilar(vip, Today, SimilarMatch.Exact, canSeeVip: true);
        Assert.Equal(("Mona Hassan", "منى حسن", "01112223333", false), (shown.FullName, shown.NameAr, shown.Mobile, shown.IdentityMasked));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("Mona", "Mona")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+201001234567", "'+201001234567")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("12 Nile St., Cairo", "\"12 Nile St., Cairo\"")]
    public void Csv_cells_are_quoted_and_formula_neutralised(string? value, string cell) => Assert.Equal(cell, ExportPatientsHandler.Cell(value));
}
