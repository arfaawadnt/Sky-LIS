using Platform.Application.Abstractions.Persistence;
using Platform.Application.Organization;
using Platform.Application.Treasury;
using Platform.Application.Treasury.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Treasury;

namespace Platform.Application.UnitTests.Treasury;

/// <summary>UAT A-04: the treasury list carries each branch's code and names, read through the Organization port.</summary>
public sealed class TreasuryListTests
{
    private static readonly Guid North = Guid.CreateVersion7();
    private static readonly Guid Gone = Guid.CreateVersion7();

    private sealed class Treasuries : ITreasuryRepository
    {
        public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) => Task.FromResult(false);

        public string? Searched { get; private set; } = "(not asked)";

        public ListSort Sorted { get; private set; }

        public Task<Paged<TreasuryListItem>> ListAsync(string? search, bool? isActive, ListSort sort, PageRequest page, CancellationToken cancellationToken)
        {
            Searched = search;
            Sorted = sort;
            return Task.FromResult(new Paged<TreasuryListItem>(
                [
                    new TreasuryListItem(Guid.CreateVersion7(), "NORTH-CASH", "North cash", "خزينة الشمال", North, true, null, null, null),
                    new TreasuryListItem(Guid.CreateVersion7(), "OLD-CASH", "Old cash", "خزينة قديمة", Gone, true, null, null, null),
                ],
                page.Page,
                page.PageSize,
                2));
        }

        public Task<TreasuryListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<TreasuryListItem?>(null);

        public Task<Domain.Treasury.Treasury?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Domain.Treasury.Treasury?>(null);

        public void Add(Domain.Treasury.Treasury aggregate)
        {
        }
    }

    private sealed class Branches : IBranchLookup
    {
        public IReadOnlyCollection<Guid>? Asked { get; private set; }

        public Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken cancellationToken) => Task.FromResult<BranchSummary?>(null);

        public Task<IReadOnlyList<BranchSummary>> ListActiveAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BranchSummary>>([]);

        public Task<bool> IsActiveAsync(Guid branchId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyDictionary<Guid, BranchSummary>> GetManyAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken)
        {
            Asked = branchIds;
            IReadOnlyDictionary<Guid, BranchSummary> known = new Dictionary<Guid, BranchSummary> { [North] = new(North, "NORTH", "North", "الشمال", "EGP", false) };
            return Task.FromResult(known);
        }
    }

    [Fact]
    public async Task Every_row_carries_its_branch_code_and_names_and_an_unknown_branch_stays_empty()
    {
        var branches = new Branches();
        var page = await new ListTreasuriesHandler(new Treasuries(), branches).Handle(new ListTreasuriesQuery(null, null, 1, 50), CancellationToken.None);

        Assert.Equal(new[] { North, Gone }, branches.Asked);
        Assert.Equal(("NORTH", "North", "الشمال"), (page.Items[0].BranchCode, page.Items[0].BranchNameEn, page.Items[0].BranchNameAr));
        Assert.Null(page.Items[1].BranchCode);
        Assert.Equal((1, 50, 2L), (page.Page, page.PageSize, page.TotalCount));
    }

    [Theory]
    [InlineData("  الشرق ", "الشرق")]
    [InlineData("east", "east")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task The_search_reaches_the_repository_trimmed_and_blank_means_no_search(string? typed, string? expected)
    {
        // UAT B-08: the register searches on the server (code and both names), so the page never filters its loaded rows only
        var treasuries = new Treasuries();
        await new ListTreasuriesHandler(treasuries, new Branches()).Handle(new ListTreasuriesQuery(typed, true, 1, 50), CancellationToken.None);

        Assert.Equal(expected, treasuries.Searched);
    }

    [Fact]
    public void A_search_longer_than_100_characters_is_refused()
    {
        var validator = new ListTreasuriesValidator();

        Assert.False(validator.Validate(new ListTreasuriesQuery(new string('x', 101), null, 1, 50)).IsValid);
        Assert.True(validator.Validate(new ListTreasuriesQuery("خزينة", null, 1, 50)).IsValid);
    }

    [Fact]
    public async Task The_sort_reaches_the_repository_and_only_allow_listed_keys_pass_validation()
    {
        // validation v0.5.0 B-03: the catalogue sorts on the server before paging, by a column of the allow-list
        var treasuries = new Treasuries();
        await new ListTreasuriesHandler(treasuries, new Branches()).Handle(new ListTreasuriesQuery(null, true, 2, 20, "nameAr", "desc"), CancellationToken.None);
        Assert.Equal(new ListSort("nameAr", Descending: true), treasuries.Sorted);

        var validator = new ListTreasuriesValidator();
        foreach (var key in TreasurySortKeys.Treasuries.Names)
        {
            Assert.True(validator.Validate(new ListTreasuriesQuery(null, null, 1, 50, key, "asc")).IsValid);
        }

        Assert.False(validator.Validate(new ListTreasuriesQuery(null, null, 1, 50, "branchId")).IsValid);
        Assert.False(validator.Validate(new ListTreasuriesQuery(null, null, 1, 50, "code", "sideways")).IsValid);
    }
}
