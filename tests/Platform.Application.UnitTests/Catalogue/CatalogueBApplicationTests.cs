using System.Text;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Commands;
using Platform.Application.Catalogue.Queries;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Catalogue;

/// <summary>Application-side behaviour of Catalogue B (intake LIS-INT-CTB-001): CSV export (A12), shape validators, the rule check (T5) and the handler refusals that need no database.</summary>
public sealed class CatalogueBApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+20 100", "'+20 100")]
    [InlineData("-5", "'-5")]
    [InlineData("@cmd", "'@cmd")]
    public void A12_cells_are_quoted_and_neutralised(string? value, string expected)
    {
        Assert.Equal(expected, CatalogueBCsv.Cell(value));
    }

    [Fact]
    public void A12_the_file_is_utf8_with_a_bom_crlf_lines_and_a_dated_name()
    {
        (string Kind, string Name)[] rows = [("Rejection", "انحلال الدم")];
        var file = CatalogueBCsv.Build("reasons", ["Kind", "Name (AR)"], rows, row => [row.Kind, row.Name], Now);

        Assert.Equal("reasons-20261009-0830.csv", file.FileName);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content.Take(3).ToArray());
        Assert.Equal("Kind,Name (AR)\r\nRejection,انحلال الدم\r\n", Encoding.UTF8.GetString(file.Content.Skip(3).ToArray()));
    }

    [Theory]
    [InlineData("ar", CsvLanguage.Ar)]
    [InlineData(" AR ", CsvLanguage.Ar)]
    [InlineData("en", CsvLanguage.En)]
    [InlineData(null, CsvLanguage.En)]
    [InlineData("fr", CsvLanguage.En)]
    public void B09_the_file_speaks_the_language_the_page_asks_for(string? lang, CsvLanguage expected)
    {
        Assert.Equal(expected, CatalogueBCsv.LanguageOf(lang));
    }

    [Fact]
    public void B09_flags_and_enum_values_read_like_the_grid_in_both_languages()
    {
        Assert.Equal(("Yes", "No"), (CatalogueBCsv.Flag(true), CatalogueBCsv.Flag(false)));
        Assert.Equal(("نعم", "لا"), (CatalogueBCsv.Flag(true, CsvLanguage.Ar), CatalogueBCsv.Flag(false, CsvLanguage.Ar)));
        Assert.Equal("Option list", CatalogueBCsv.Label(CsvLanguage.En, "answerType", "OptionList"));
        Assert.Equal("نعم / لا", CatalogueBCsv.Label(CsvLanguage.Ar, "answerType", "YesNo"));
        Assert.Equal("All patients", CatalogueBCsv.Label(CsvLanguage.En, "gender", "Any"));
        Assert.Equal("Text report", CatalogueBCsv.Label(CsvLanguage.En, "templateType", "Text"));
        Assert.Equal("Text", CatalogueBCsv.Label(CsvLanguage.En, "answerType", "Text"));
        Assert.Equal("Unknown", CatalogueBCsv.Label(CsvLanguage.Ar, "answerType", "Unknown"));
    }

    [Fact]
    public void B09_an_arabic_file_has_arabic_headers()
    {
        (string Code, bool Active)[] rows = [("FAST", true)];
        var file = CatalogueBCsv.Build("preparations", ["Code", "Active"], rows, row => [row.Code, CatalogueBCsv.Flag(row.Active, CsvLanguage.Ar)], Now, CsvLanguage.Ar);

        Assert.Equal("الرمز,نشط\r\nFAST,نعم\r\n", Encoding.UTF8.GetString(file.Content.Skip(3).ToArray()));
    }

    [Fact]
    public async Task A12_every_page_is_read_until_the_last()
    {
        var all = Enumerable.Range(1, 450).ToList();
        var asked = new List<int>();

        var rows = await CatalogueBCsv.AllAsync(page =>
        {
            asked.Add(page.Page);
            var items = all.Skip(page.Skip).Take(page.PageSize).ToList();
            return Task.FromResult(new Paged<int>(items, page.Page, page.PageSize, all.Count));
        });

        Assert.Equal(450, rows.Count);
        Assert.Equal([1, 2, 3], asked);
    }

    [Fact]
    public void Validators_refuse_unknown_enum_names_and_a_header_in_one_language()
    {
        var preparation = new CreatePreparationValidator().Validate(new CreatePreparationCommand("FAST", "Q", "س", "Essay", null, "Any", null));
        Assert.Contains(preparation.Errors, error => error.PropertyName == nameof(CreatePreparationCommand.AnswerType));

        var template = new CreateReportTemplateValidator().Validate(new CreateReportTemplateCommand("A4", "أ4", "Results", false, "Both", "Header only", null, null, null));
        Assert.Contains(template.Errors, error => error.PropertyName == nameof(CreateReportTemplateCommand.HeaderTextAr));

        Assert.False(new ListReasonsValidator().Validate(new ListReasonsQuery(null, null, "flying", null, null)).IsValid);
        Assert.True(new ListReasonsValidator().Validate(new ListReasonsQuery(null, true, "cancellation", 1, 200)).IsValid);
        Assert.False(new CreateShortcutSetValidator().Validate(new CreateShortcutSetCommand("ICU", "ICU", "عناية", [])).IsValid);
    }

    [Fact]
    public async Task S_CTB_T5_the_rule_check_reports_unknown_and_inactive_services_and_saves_nothing()
    {
        var known = Guid.CreateVersion7();
        var inactive = Guid.CreateVersion7();
        var unknown = Guid.CreateVersion7();
        var references = new FakeReferences
        {
            Services =
            {
                [known] = new LinkedServiceView(known, "GLU", "Glucose", "جلوكوز", true),
                [inactive] = new LinkedServiceView(inactive, "OLD", "Old test", "قديم", false),
            },
        };

        var result = await new ValidateTestRuleHandler(new FakeRules(), references).Handle(new ValidateTestRuleQuery(null, [known], [inactive, unknown]), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Error", result.Issues[0].Severity);
        Assert.Contains(result.Issues, issue => issue.Code == CatalogueBCodes.ServiceUnknown && issue.ServiceId == unknown);
        Assert.Contains(result.Issues, issue => issue.Code == CatalogueBCodes.TestRuleInactiveService && issue.Severity == "Warning" && issue.ServiceId == inactive);
    }

    [Fact]
    public async Task S_CTB_T1_ref_a_tier_of_another_payer_is_refused_CAT_207_before_anything_is_staged()
    {
        var labs = new FakeSendingLabs();
        var handler = new CreateSendingLabHandler(labs, new FakeDoctors(), new FakeReferences { PayerExists = true, TierOfPayer = false }, new FakeUnitOfWork(), new FixedClock(Now));

        var refusal = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new CreateSendingLabCommand("Delta Lab", "معمل دلتا", Guid.CreateVersion7(), Guid.CreateVersion7(), "Ahmed", "أحمد", null, null), CancellationToken.None));

        Assert.Equal(CatalogueBCodes.ContractTierUnknown, refusal.Code);
        Assert.Empty(labs.Added);
    }

    [Fact]
    public async Task S_CTB_T1_ref_an_unknown_service_is_refused_CAT_203()
    {
        var handler = new CreateDiagnosisHandler(new FakeDiagnoses(), new FakeReferences(), new FakeUnitOfWork(), new FixedClock(Now));

        var refusal = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CreateDiagnosisCommand("E11.9", "Diabetes", "سكري", [Guid.CreateVersion7()]), CancellationToken.None));

        Assert.Equal((CatalogueBCodes.ServiceUnknown, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);
    }

    private sealed class FakeReferences : ICatalogueBReferences
    {
        public Dictionary<Guid, LinkedServiceView> Services { get; } = [];

        public bool PayerExists { get; init; }

        public bool TierOfPayer { get; init; }

        public Task<IReadOnlyList<Guid>> MissingServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(serviceIds.Where(id => !Services.ContainsKey(id)).ToList());

        public Task<IReadOnlyDictionary<Guid, LinkedServiceView>> ServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, LinkedServiceView>>(Services.Where(pair => serviceIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));

        public Task<bool> ClinicalGroupExistsAsync(Guid clinicalGroupId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<Guid>> MissingBranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<bool> PayerExistsAsync(Guid payerId, CancellationToken cancellationToken) => Task.FromResult(PayerExists);

        public Task<bool> ContractTierOfPayerExistsAsync(Guid contractTierId, Guid payerId, CancellationToken cancellationToken) => Task.FromResult(TierOfPayer);
    }

    private sealed class FakeRules : ITestRuleRepository
    {
        public Task<TestRule?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<TestRule?>(null);

        public void Add(TestRule aggregate) => throw new InvalidOperationException("The rule check saves nothing.");

        public Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<TestRule>> ActiveRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TestRule>>([TestRule.Create(new LocalizedText("Existing", "قائمة"), null, [Guid.CreateVersion7()], [Guid.CreateVersion7()], Now)]);

        public Task<Paged<TestRuleListItem>> ListAsync(CatalogueBFilter filter, Guid? clinicalGroupId, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TestRuleListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSendingLabs : ISendingLabRepository
    {
        public List<SendingLab> Added { get; } = [];

        public Task<SendingLab?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<SendingLab?>(null);

        public void Add(SendingLab aggregate) => Added.Add(aggregate);

        public Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Paged<SendingLabListItem>> ListAsync(CatalogueBFilter filter, Guid? payerId, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SendingLabListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDoctors : IDoctorRepository
    {
        public Task<Doctor?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Doctor?>(null);

        public void Add(Doctor aggregate) => throw new NotSupportedException();

        public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Paged<DoctorListItem>> ListAsync(CatalogueBFilter filter, Guid? branchId, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DoctorListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDiagnoses : IDiagnosisRepository
    {
        public Task<Diagnosis?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Diagnosis?>(null);

        public void Add(Diagnosis aggregate) => throw new InvalidOperationException("Nothing may be staged after a refusal.");

        public Task<bool> IcdCodeExistsAsync(string icdCode, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Paged<DiagnosisListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DiagnosisListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
