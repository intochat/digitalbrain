using System.Net;
using System.Text;
using System.Text.Json;
using Salesforce.Force;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public sealed class SalesforceApiClientTests
{
    [Fact]
    public async Task Current_profile_uses_OAuth_identity_endpoint_and_returns_bounded_fields()
    {
        var (client, handler) = CreateClient();

        var profile = await client.GetCurrentUserProfileAsync(CancellationToken.None);
        using var json = JsonDocument.Parse(profile);

        Assert.Equal("Ada Lovelace", json.RootElement.GetProperty("DisplayName").GetString());
        Assert.Equal("ada@example.com", json.RootElement.GetProperty("Email").GetString());
        Assert.DoesNotContain("photos", profile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(handler.Requests, request => request.AbsolutePath == "/id/org/user");
    }

    [Fact]
    public async Task Current_profile_without_identity_endpoint_requires_reconnection_without_network_call()
    {
        var (client, handler) = CreateClient(identityUrl: null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetCurrentUserProfileAsync(CancellationToken.None));

        Assert.Contains("Reconnect Salesforce", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://login.salesforce.com/id/org/user")]
    [InlineData("https://attacker.example/id/org/user")]
    [InlineData("https://login.salesforce.com/services/data/v60.0")]
    public async Task Current_profile_rejects_untrusted_identity_endpoint_without_forwarding_token(string identityUrl)
    {
        var (client, handler) = CreateClient(identityUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetCurrentUserProfileAsync(CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0, "LIMIT 1")]
    [InlineData(10, "LIMIT 10")]
    [InlineData(999, "LIMIT 50")]
    public async Task Account_reads_are_fixed_field_ordered_and_bounded(int requested, string expectedLimit)
    {
        var (client, handler) = CreateClient();

        var accounts = await client.ListAccountsAsync(requested, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        var query = Uri.UnescapeDataString(request.Query);
        Assert.Contains("FROM Account ORDER BY LastModifiedDate DESC", query, StringComparison.Ordinal);
        Assert.Contains(expectedLimit, query, StringComparison.Ordinal);
        Assert.DoesNotContain("attributes", Assert.Single(accounts), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contact_reads_include_parent_account_and_never_accept_caller_SOQL()
    {
        var (client, handler) = CreateClient();

        var contacts = await client.ListContactsAsync(7, CancellationToken.None);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("Account.Name", query, StringComparison.Ordinal);
        Assert.Contains("LIMIT 7", query, StringComparison.Ordinal);
        Assert.Contains("Example Account", Assert.Single(contacts), StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(ISalesforceApiClient).GetMethods(),
            method => method.Name.Contains("Query", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Crm_schema_is_allowlisted_to_account_and_contact_and_summarizes_accessible_fields()
    {
        var (client, handler) = CreateClient();

        var schema = await client.DescribeCrmAccessAsync(CancellationToken.None);
        using var json = JsonDocument.Parse(schema);

        Assert.Equal(["Account", "Contact"], handler.Requests.Select(request => request.Segments[^2].TrimEnd('/')).ToArray());
        Assert.True(json.RootElement.GetProperty("Account").GetProperty("Queryable").GetBoolean());
        Assert.Equal("Name", json.RootElement.GetProperty("Contact").GetProperty("AccessibleFields")[0].GetProperty("Name").GetString());
        Assert.DoesNotContain("createable", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updateable", schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_prevents_provider_request()
    {
        var (client, handler) = CreateClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ListAccountsAsync(10, cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    private static (SalesforceApiClient Client, RecordingSalesforceHandler Handler) CreateClient(
        string? identityUrl = "https://login.salesforce.com/id/org/user")
    {
        var handler = new RecordingSalesforceHandler();
        var jsonHttp = new HttpClient(handler, disposeHandler: false);
        var xmlHttp = new HttpClient(handler, disposeHandler: false);
        var force = new ForceClient(
            "https://example.my.salesforce.com",
            "access-token",
            "v60.0",
            jsonHttp,
            xmlHttp,
            callerWillDisposeHttpClients: true);
        return (new SalesforceApiClient(force, identityUrl), handler);
    }

    private sealed class RecordingSalesforceHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            var uri = request.RequestUri!;
            var body = uri.AbsolutePath switch
            {
                "/id/org/user" => """
                    {"user_id":"005-user","organization_id":"00D-org","display_name":"Ada Lovelace","username":"ada@example.com","email":"ada@example.com","user_type":"STANDARD","active":true,"locale":"en_US","language":"en_US","photos":{"picture":"secret"}}
                    """,
                var path when path.Contains("/query", StringComparison.Ordinal) &&
                                  Uri.UnescapeDataString(uri.Query).Contains("FROM Contact", StringComparison.Ordinal) => """
                    {"totalSize":1,"done":true,"records":[{"attributes":{"type":"Contact"},"Id":"003-contact","Name":"Grace Hopper","Account":{"attributes":{"type":"Account"},"Name":"Example Account"}}]}
                    """,
                var path when path.Contains("/query", StringComparison.Ordinal) => """
                    {"totalSize":1,"done":true,"records":[{"attributes":{"type":"Account"},"Id":"001-account","Name":"Example Account"}]}
                    """,
                var path when path.TrimEnd('/').EndsWith("/sobjects/Account/describe", StringComparison.Ordinal) => Describe("Account", "Account Name"),
                var path when path.TrimEnd('/').EndsWith("/sobjects/Contact/describe", StringComparison.Ordinal) => Describe("Contact", "Full Name"),
                _ => throw new InvalidOperationException("Unexpected Salesforce request: " + uri)
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string Describe(string name, string fieldLabel) =>
            $$"""
            {"name":"{{name}}","label":"{{name}}","queryable":true,"searchable":true,"createable":true,"updateable":true,"fields":[{"name":"Name","label":"{{fieldLabel}}","type":"string","createable":true,"updateable":true}]}
            """;
    }
}
