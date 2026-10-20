using Platform.Infrastructure.Security;

namespace Platform.Application.UnitTests.Security;

/// <summary>
/// LIS-CR-005: <see cref="ConfigGuard.InspectPortalBaseUrl"/> — an absolute URL without path, query, fragment or trailing slash;
/// required and https in Production; unset (the development default) or http elsewhere.
/// </summary>
public sealed class PortalBaseUrlConfigGuardTests
{
    [Theory]
    [InlineData("https://lis.example.com", true)]
    [InlineData("https://10.0.0.5:5443", true)]
    [InlineData("http://127.0.0.1:4300", false)]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    public void Well_formed_values_pass_outside_production(string? value, bool production)
    {
        Assert.Empty(ConfigGuard.InspectPortalBaseUrl(value, production));
    }

    [Theory]
    [InlineData("lis.example.com")]
    [InlineData("/lab")]
    [InlineData("ftp://lis.example.com")]
    [InlineData("https://lis.example.com/")]
    [InlineData("https://lis.example.com/portal")]
    [InlineData("https://lis.example.com?x=1")]
    [InlineData("https://lis.example.com#top")]
    [InlineData("https://user:secret@lis.example.com")]
    public void A_malformed_value_is_refused_in_every_environment(string value)
    {
        foreach (var production in new[] { false, true })
        {
            var violation = Assert.Single(ConfigGuard.InspectPortalBaseUrl(value, production));
            Assert.Contains(ConfigGuard.PortalBaseUrlKey, violation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Production_requires_an_https_value()
    {
        Assert.Contains("required", Assert.Single(ConfigGuard.InspectPortalBaseUrl(null, production: true)), StringComparison.Ordinal);
        Assert.Contains("https", Assert.Single(ConfigGuard.InspectPortalBaseUrl("http://lis.example.com", production: true)), StringComparison.Ordinal);
        Assert.Empty(ConfigGuard.InspectPortalBaseUrl("http://lis.example.com", production: false));
        Assert.Equal("Lis:Platform:PortalBaseUrl", ConfigGuard.PortalBaseUrlKey);
        Assert.Empty(ConfigGuard.InspectPortalBaseUrl(ConfigGuard.DefaultPortalBaseUrl, production: false));
    }
}
