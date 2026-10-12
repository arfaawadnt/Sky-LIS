using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Queries;
using Platform.Application.Organization;
using Platform.Application.Organization.Queries;
using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Organization;

/// <summary>
/// Validation v0.5.0 (client set-up): the branch choice of a user (B-02, ORG-018) and the server-side sort of the
/// paged registers (B-03): allow-listed keys only, applied before paging with stable tie-breakers.
/// </summary>
public sealed class SetupFixRulesTests
{
    private sealed record Row(int Id, string Name, int Order);

    private static readonly SortKeys RowKeys = new("name", "order");

    private static readonly Row[] Rows = [new(1, "beta", 2), new(2, "alpha", 2), new(3, "gamma", 1), new(4, "alpha", 1)];

    private static SortColumns<Row> Columns() => new SortColumns<Row>(RowKeys)
        .Column("name", row => row.Name)
        .Column("order", row => row.Order)
        .ThenBy(row => row.Id);

    [Fact]
    public void B_02_restricted_with_no_branch_or_every_branch_with_branches_is_ORG_018()
    {
        Assert.Equal(OrganizationCodes.BranchChoiceRequired, Assert.Throws<DomainException>(() => OrganizationGuards.EnsureBranchChoice(false, [])).Code);
        Assert.Equal(OrganizationCodes.BranchChoiceRequired, Assert.Throws<DomainException>(() => OrganizationGuards.EnsureBranchChoice(false, [Guid.Empty])).Code);
        Assert.Equal(OrganizationCodes.BranchChoiceRequired, Assert.Throws<DomainException>(() => OrganizationGuards.EnsureBranchChoice(false, null)).Code);
        Assert.Equal(OrganizationCodes.BranchChoiceRequired, Assert.Throws<DomainException>(() => OrganizationGuards.EnsureBranchChoice(true, [Guid.CreateVersion7()])).Code);

        OrganizationGuards.EnsureBranchChoice(false, [Guid.CreateVersion7()]);
        OrganizationGuards.EnsureBranchChoice(true, []);
        OrganizationGuards.EnsureBranchChoice(null, []);   // the older contract: empty = every branch
        OrganizationGuards.EnsureBranchChoice(null, [Guid.CreateVersion7()]);
    }

    [Fact]
    public void B_03_the_requested_column_orders_the_whole_list_with_the_tie_breaker_after_it()
    {
        var descending = Columns().Apply(Rows.AsQueryable(), ListSort.Of("NAME", "desc"), rows => rows.OrderBy(row => row.Id)).ToList();
        Assert.Equal([3, 1, 2, 4], descending.Select(row => row.Id));

        var byOrder = Columns().Apply(Rows.AsQueryable(), ListSort.Of("order", null), rows => rows.OrderBy(row => row.Id)).ToList();
        Assert.Equal([3, 4, 1, 2], byOrder.Select(row => row.Id));

        // A page of the sorted list is a slice of the whole order, not a re-order of the slice (the defect).
        var page2 = Columns().Apply(Rows.AsQueryable(), ListSort.Of("name", "asc"), rows => rows.OrderBy(row => row.Id)).Skip(2).Take(2).ToList();
        Assert.Equal([1, 3], page2.Select(row => row.Id));
    }

    [Fact]
    public void R_09_a_text_column_sorts_without_regard_to_case_and_keeps_the_tie_breaker()
    {
        // Byte order puts "eGFR" and "hCG" after "Sodium"; every list orders names case-insensitively (validation v0.5.0 R-09).
        Row[] tests = [new(1, "Sodium", 0), new(2, "hCG", 0), new(3, "eGFR", 0), new(4, "Albumin", 0), new(5, "HCG", 0), new(6, "albumin", 0)];
        var ascending = Columns().Apply(tests.AsQueryable(), ListSort.Of("name", "asc"), rows => rows.OrderBy(row => row.Id)).ToList();
        Assert.Equal([4, 6, 3, 2, 5, 1], ascending.Select(row => row.Id));   // equal names in the tie-breaker's order (id)

        var descending = Columns().Apply(tests.AsQueryable(), ListSort.Of("name", "desc"), rows => rows.OrderBy(row => row.Id)).ToList();
        Assert.Equal([1, 2, 5, 3, 4, 6], descending.Select(row => row.Id));

        // A null text sorts like the provider's null (first ascending in LINQ to objects) instead of throwing.
        var withNull = new SortColumns<NullableRow>(new SortKeys("name")).Column("name", row => row.Name).ThenBy(row => row.Id);
        NullableRow[] rows = [new(1, "beta"), new(2, null), new(3, "Alpha")];
        Assert.Equal([2, 3, 1], withNull.Apply(rows.AsQueryable(), ListSort.Of("name", "asc"), query => query.OrderBy(row => row.Id)).Select(row => row.Id));
    }

    private sealed record NullableRow(int Id, string? Name);

    [Fact]
    public void B_03_no_sort_keeps_the_register_order_and_a_key_off_the_list_is_refused()
    {
        var fallback = Columns().Apply(Rows.AsQueryable(), ListSort.None, rows => rows.OrderByDescending(row => row.Id)).ToList();
        Assert.Equal([4, 3, 2, 1], fallback.Select(row => row.Id));
        Assert.Equal(ListSort.None, ListSort.Of("  ", "desc"));

        Assert.Throws<ArgumentException>(() => Columns().Apply(Rows.AsQueryable(), ListSort.Of("id", "asc"), rows => rows.OrderBy(row => row.Id)));
        Assert.Throws<InvalidOperationException>(() => new SortColumns<Row>(RowKeys).Column("id", row => row.Id));

        // A key of the allow-list without an expression is caught on first use, whatever is asked.
        var incomplete = new SortColumns<Row>(RowKeys).Column("name", row => row.Name);
        Assert.Throws<InvalidOperationException>(() => incomplete.Apply(Rows.AsQueryable(), ListSort.None, rows => rows.OrderBy(row => row.Id)));
    }

    [Fact]
    public void B_03_the_list_validators_accept_only_their_allow_listed_keys_and_directions()
    {
        var units = new ListMeasurementUnitsValidator();
        Assert.True(units.Validate(new ListMeasurementUnitsQuery(null, true, 2, 20, "name", "desc")).IsValid);
        Assert.True(units.Validate(new ListMeasurementUnitsQuery(null, true, 2, 20, "Name", "ASC")).IsValid);
        Assert.True(units.Validate(new ListMeasurementUnitsQuery(null, true, 2, 20)).IsValid);
        Assert.Contains(units.Validate(new ListMeasurementUnitsQuery(null, true, 2, 20, "id", "desc")).Errors, error => error.PropertyName == nameof(ListMeasurementUnitsQuery.Sort));
        Assert.Contains(units.Validate(new ListMeasurementUnitsQuery(null, true, 2, 20, "name", "down")).Errors, error => error.PropertyName == nameof(ListMeasurementUnitsQuery.Direction));

        var users = new ListUsersValidator();
        Assert.True(users.Validate(new ListUsersQuery(null, null, null, 1, 50, "nameAr", "desc")).IsValid);
        Assert.False(users.Validate(new ListUsersQuery(null, null, null, 1, 50, "email", "desc")).IsValid);
        Assert.Equal(["code", "nameEn", "nameAr"], OrganizationSortKeys.Branches.Names);
        Assert.Contains("drugTypeAr", CatalogueSortKeys.CommercialNames.Names);
    }
}
