using System.Text.RegularExpressions;
using Platform.Domain.Authorization;

namespace Platform.Domain.UnitTests.Authorization;

public sealed partial class PermissionCatalogTests
{
    [Fact]
    public void Keys_are_unique_and_shaped_module_dot_action()
    {
        var keys = PermissionCatalog.All.SelectMany(module => module.Keys).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.Matches(KeyShape(), key));
        Assert.Equal(keys.Count, PermissionCatalog.Keys.Count);
    }

    [Fact]
    public void Known_keys_are_recognised_and_unknown_ones_are_not()
    {
        Assert.True(PermissionCatalog.IsKnown(PermissionCatalog.Key(PermissionCatalog.Platform, PermissionAction.Manage)));
        Assert.True(PermissionCatalog.IsKnown("audit.export"));
        Assert.True(PermissionCatalog.IsKnown("organization.export"));   // UAT R1-05: registers export under the module's export key
        Assert.True(PermissionCatalog.IsKnown("billing.export"));
        Assert.False(PermissionCatalog.IsKnown("treasury.view"));   // LIS-CR-004: the treasury group is withdrawn
        Assert.False(PermissionCatalog.IsKnown("audit.manage"));
        Assert.False(PermissionCatalog.IsKnown("Platform.Manage"));
        Assert.False(PermissionCatalog.IsKnown(string.Empty));
    }

    [Fact]
    public void Action_keys_are_lower_camel_case()
    {
        Assert.Equal("x.viewConfidential", PermissionCatalog.Key("x", PermissionAction.ViewConfidential));
        Assert.Equal("x.view", PermissionCatalog.Key("x", PermissionAction.View));
        Assert.Throws<ArgumentException>(() => PermissionCatalog.Key(" ", PermissionAction.View));
    }

    [Fact]
    public void A_module_needs_a_key_a_name_and_at_least_one_action()
    {
        Assert.Throws<ArgumentException>(() => new PermissionModule("x", "X"));
        Assert.Throws<ArgumentException>(() => new PermissionModule(" ", "X", PermissionAction.View));
        Assert.Single(new PermissionModule("x", "X", PermissionAction.View, PermissionAction.View).Actions);
    }

    [GeneratedRegex("^[a-z]+\\.[a-z][a-zA-Z]*$")]
    private static partial Regex KeyShape();
}
