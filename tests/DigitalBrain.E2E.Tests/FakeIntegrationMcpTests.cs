using ModelContextProtocol.Client;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Integrations.Gmail;
using DigitalBrain.Integrations.Salesforce;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class FakeIntegrationMcpTests(AppHostFixture fixture)
{
    private static readonly string[] GmailTools =
    [
        "create_draft",
        "get_message",
        "get_thread",
        "label_message",
        "label_thread",
        "list_drafts",
        "list_labels",
        "search_threads",
        "unlabel_message",
        "unlabel_thread",
    ];

    private static readonly string[] SalesforceTools =
    [
        "createRecord",
        "getObjectSchema",
        "soqlQuery",
        "soslSearch",
        "updateRecord",
        "updateRelatedRecord",
    ];

    [Fact]
    public async Task GmailFakePublishesTheOfficialCatalogAndDeterministicThread()
    {
        await using var client = await CreateClient("fake-gmail-mcp");

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(GmailTools, tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, static tool => Assert.DoesNotContain(
            ":true",
            Assert.NotNull(tool.ReturnJsonSchema).GetRawText(),
            StringComparison.Ordinal));

        var result = await client.CallToolAsync(
            "search_threads",
            new Dictionary<string, object?> { ["query"] = "from:vlad@intochat.io" },
            cancellationToken: TestContext.Current.CancellationToken);

        var json = Assert.NotNull(result.StructuredContent);
        var thread = Assert.Single(json.GetProperty("threads").EnumerateArray());
        Assert.Equal("thread-intochat", thread.GetProperty("id").GetString());
        var message = Assert.Single(thread.GetProperty("messages").EnumerateArray());
        Assert.Equal("vlad@intochat.io", message.GetProperty("sender").GetString());
    }

    [Fact]
    public async Task SalesforceFakePublishesMutationCatalogAndUpdatesTheMatchingAccount()
    {
        await using var client = await CreateClient("fake-salesforce-mcp");

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SalesforceTools, tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, static tool => Assert.DoesNotContain(
            ":true",
            Assert.NotNull(tool.ReturnJsonSchema).GetRawText(),
            StringComparison.Ordinal));

        var query = await client.CallToolAsync(
            "soqlQuery",
            new Dictionary<string, object?>
            {
                ["query"] = "SELECT Id, Name, Description FROM Account WHERE Website = 'https://intochat.io' LIMIT 2",
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var account = Assert.Single(Assert.NotNull(query.StructuredContent)
            .GetProperty("records").EnumerateArray());
        Assert.Equal("001INTOCHAT", account.GetProperty("Id").GetString());

        var updated = await client.CallToolAsync(
            "updateRecord",
            new Dictionary<string, object?>
            {
                ["sobjectName"] = "Account",
                ["id"] = "001INTOCHAT",
                ["body"] = new Dictionary<string, object?>
                {
                    ["Description"] = "Verified customer conversation platform.",
                },
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var mutation = Assert.NotNull(updated.StructuredContent);
        Assert.True(mutation.GetProperty("success").GetBoolean());
        Assert.Equal("001INTOCHAT", mutation.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GenericClientAndAdaptersUseTheSameMcpProtocolAsRealProviders()
    {
        var mcp = new McpIntegrationClient();
        var gmail = new McpGmailTransport(mcp, EndpointFor("fake-gmail-mcp"));
        var salesforce = new McpSalesforceTransport(mcp, EndpointFor("fake-salesforce-mcp"));

        var gmailJson = await gmail.SearchJsonAsync(
            "vlad@intochat.io",
            "new email",
            TestContext.Current.CancellationToken);
        using var gmailDocument = System.Text.Json.JsonDocument.Parse(gmailJson);
        var thread = Assert.Single(gmailDocument.RootElement.GetProperty("threads").EnumerateArray());
        Assert.Equal("thread-intochat", thread.GetProperty("id").GetString());

        var salesforceJson = await salesforce.UpsertJsonAsync(
            "Account",
            """{"id":"001INTOCHAT","body":{"Description":"Verified customer conversation platform."}}""",
            TestContext.Current.CancellationToken);
        using var salesforceDocument = System.Text.Json.JsonDocument.Parse(salesforceJson);
        Assert.True(salesforceDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("001INTOCHAT", salesforceDocument.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GenericClientRejectsToolsOutsideTheServerCatalog()
    {
        var mcp = new McpIntegrationClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mcp.CallAsync(
            EndpointFor("fake-salesforce-mcp"),
            "delete_everything",
            new Dictionary<string, object?>(),
            TestContext.Current.CancellationToken));

        Assert.Contains("delete_everything", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fake-salesforce-mcp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SalesforceFakePersistsMutationsForSubsequentQueries()
    {
        await using var client = await CreateClient("fake-salesforce-mcp");
        await client.CallToolAsync(
            "updateRecord",
            new Dictionary<string, object?>
            {
                ["id"] = "001ACME",
                ["body"] = new Dictionary<string, object?> { ["Description"] = "Persisted by fake MCP." },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var query = await client.CallToolAsync(
            "soqlQuery",
            new Dictionary<string, object?>
            {
                ["query"] = "SELECT Id, Description FROM Account WHERE Id = '001ACME' LIMIT 1",
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var account = Assert.Single(Assert.NotNull(query.StructuredContent)
            .GetProperty("records").EnumerateArray());
        Assert.Equal("Persisted by fake MCP.", account.GetProperty("Description").GetString());
    }

    private async Task<McpClient> CreateClient(string resource)
    {
        var http = fixture.CreateHttpClient(resource, "http");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    private McpIntegrationEndpoint EndpointFor(string resource)
    {
        using var http = fixture.CreateHttpClient(resource, "http");
        return new McpIntegrationEndpoint(resource, new Uri(http.BaseAddress!, "/mcp"));
    }
}
