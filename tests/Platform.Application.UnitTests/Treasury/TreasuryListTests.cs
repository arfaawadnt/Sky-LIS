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

        public Task<Paged<TreasuryListItem>> ListAsync(bool? isActive, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new Paged<TreasuryListItem>(
                [
                    new TreasuryListItem(Guid.CreateVersion7(), "NORTH-CASH", "North cash", "خزينة الشمال", North, true, null, null, null),
                    new TreasuryListItem(Guid.CreateVersion7(), "OLD-CASH", "Old cash", "خزينة قديمة", Gone, true, null, null, null),
                ],
                page.Page,
                page.PageSize,
                2));

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
        var page = await new ListTreasuriesHandler(new Treasuries(), branches).Handle(new ListTreasuriesQuery(null, 1, 50), CancellationToken.None);

        Assert.Equal(new[] { North, Gone }, branches.Asked);
        Assert.Equal(("NORTH", "North", "الشمال"), (page.Items[0].BranchCode, page.Items[0].BranchNameEn, page.Items[0].BranchNameAr));
        Assert.Null(page.Items[1].BranchCode);
        Assert.Equal((1, 50, 2L), (page.Page, page.PageSize, page.TotalCount));
    }
}
