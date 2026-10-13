using Platform.Application.Catalogue.Queries;
using Platform.Application.Organization;
using Platform.Contracts.Catalogue;

namespace Platform.Application.UnitTests.Catalogue;

/// <summary>Validation v0.5.0: the doctors register returns the code and name of every branch it shows, whatever the reader's branches.</summary>
public sealed class DoctorBranchesTests
{
    private static readonly Guid Main = Guid.CreateVersion7();
    private static readonly Guid East = Guid.CreateVersion7();
    private static readonly Guid Gone = Guid.CreateVersion7();

    [Fact]
    public async Task Each_doctor_gets_its_branches_in_its_own_order_and_an_unknown_branch_is_left_out()
    {
        var lookup = new Branches();
        var everywhere = Doctor("D0", []);
        var two = Doctor("D1", [East, Main, Gone]);

        var joined = await DoctorBranches.JoinAsync(lookup, [everywhere, two], CancellationToken.None);

        Assert.Empty(joined[0].Branches);
        Assert.Equal(["V14-EAST", "MAIN"], joined[1].Branches.Select(branch => branch.Code).ToArray());
        Assert.Equal(("الفرع الشرقي", East), (joined[1].Branches[0].NameAr, joined[1].Branches[0].Id));
        Assert.Equal(1, lookup.Calls);   // one read for the page
    }

    [Fact]
    public async Task A_page_without_branches_reads_nothing()
    {
        var lookup = new Branches();

        var joined = await DoctorBranches.JoinAsync(lookup, [Doctor("D0", [])], CancellationToken.None);

        Assert.Single(joined);
        Assert.Equal(0, lookup.Calls);
    }

    private static DoctorListItem Doctor(string code, IReadOnlyList<Guid> branchIds) =>
        new(Guid.CreateVersion7(), code, "Dr. " + code, "د. " + code, null, null, branchIds, 10m, null, true, []);

    private sealed class Branches : IBranchLookup
    {
        public int Calls { get; private set; }

        public Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<BranchSummary>> ListActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsActiveAsync(Guid branchId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, BranchSummary>> GetManyAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken)
        {
            Calls++;
            var all = new[]
            {
                new BranchSummary(Main, "MAIN", "Main", "الرئيسي", "EGP", true),
                new BranchSummary(East, "V14-EAST", "East", "الفرع الشرقي", "EGP", false),
            };
            IReadOnlyDictionary<Guid, BranchSummary> found = all.Where(branch => branchIds.Contains(branch.Id)).ToDictionary(branch => branch.Id);
            return Task.FromResult(found);
        }
    }
}
