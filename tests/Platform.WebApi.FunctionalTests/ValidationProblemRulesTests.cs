using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Platform.Contracts.Account;
using Platform.Contracts.Common;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// C-06: a <c>VALIDATION-400</c> carries, beside the unchanged <c>errors</c> messages, a machine-readable <c>rules</c>
/// entry per field (the failed rule and its numeric arguments), so the SPA shows its own localized, field-aware text
/// instead of the validator's English. A body the binder cannot read reports the <c>Format</c> rule.
/// </summary>
public sealed class ValidationProblemRulesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Tickets = "/api/v1/support/tickets";

    [DatabaseFact]
    public async Task A_validation_failure_reports_the_rule_of_each_field_beside_its_message()
    {
        var client = await SignedInClientAsync(["support.view", "support.create"]);

        var response = await client.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest("", new string('x', 10), "High"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(ProblemCodes.ValidationFailed, root.GetProperty("code").GetString());

        // the existing shape is unchanged: field → messages
        var messages = root.GetProperty("errors").GetProperty("subject");
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.Equal(JsonValueKind.String, messages[0].ValueKind);

        // the new extension: field → rules, the rule without the "Validator" suffix
        var subjectRules = root.GetProperty("rules").GetProperty("subject");
        Assert.Equal("NotEmpty", subjectRules[0].GetProperty("rule").GetString());
        Assert.False(root.GetProperty("rules").TryGetProperty("description", out _));
    }

    [DatabaseFact]
    public async Task A_length_rule_carries_its_numeric_bounds_and_never_a_request_value()
    {
        var client = await SignedInClientAsync(["support.view", "support.create"]);

        var response = await client.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest(new string('s', 2000), "Details", "High"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rule = document.RootElement.GetProperty("rules").GetProperty("subject")[0];
        Assert.Equal("MaximumLength", rule.GetProperty("rule").GetString());
        Assert.True(rule.GetProperty("maxLength").GetDecimal() > 0);
        Assert.False(rule.TryGetProperty("totalLength", out _));   // the length of what the user typed is request data
        Assert.False(rule.TryGetProperty("propertyValue", out _));
    }

    [DatabaseFact]
    public async Task A_body_the_binder_cannot_read_reports_the_format_rule()
    {
        var client = await SignedInClientAsync(["support.view", "support.create"]);

        using var content = new StringContent("{\"subject\": 12, \"description\": [] ", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(Tickets, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProblemCodes.ValidationFailed, document.RootElement.GetProperty("code").GetString());
        foreach (var field in document.RootElement.GetProperty("rules").EnumerateObject())
        {
            Assert.Equal("Format", field.Value[0].GetProperty("rule").GetString());
        }
    }

    private async Task<HttpClient> SignedInClientAsync(IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await factory.SeedUserAsync(permissions: permissions);
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken!);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
