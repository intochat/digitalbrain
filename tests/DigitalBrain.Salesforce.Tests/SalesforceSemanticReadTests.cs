using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.V2;
using Salesforce.Force;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public sealed class SalesforceSemanticReadTests
{
    [Fact]
    public async Task Semantic_read_resolves_described_labels_and_adds_record_id_tie_breaker()
    {
        var (client, handler) = CreateClient();
        var request = new V2SalesforceRecordReadRequest(
            new V2SalesforceSemanticEntity("Accounts"),
            Fields: [new V2SalesforceSemanticField("Account Name")],
            Filters:
            [
                new V2SalesforceFilter(
                    new V2SalesforceSemanticField("Industry"),
                    V2SemanticFilterOperator.Equals,
                    "Technology' OR Name != 'Acme")
            ],
            Sorts:
            [
                new V2SalesforceSort(
                    new V2SalesforceSemanticField("Account Name"),
                    V2SemanticSortDirection.Descending)
            ],
            Limit: 500);

        var page = await client.ReadRecordsAsync(request, CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.Requests.Single(uri => uri.AbsolutePath.EndsWith("/query", StringComparison.Ordinal)).Query);
        Assert.Contains("SELECT Id, Name FROM Account", query, StringComparison.Ordinal);
        Assert.Contains("Industry = 'Technology\\' OR Name != \\'Acme'", query, StringComparison.Ordinal);
        Assert.Contains("ORDER BY Name DESC, Id ASC LIMIT 200", query, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM Accounts", query, StringComparison.Ordinal);
        Assert.Equal("00D000000000001", page.Scope.OrganizationId);
        Assert.Equal(1, page.ReturnedCount);
        Assert.NotNull(page.Continuation);
        Assert.DoesNotContain(
            typeof(SalesforceContinuation).GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    [Fact]
    public async Task Semantic_labels_fail_closed_before_unresolved_names_reach_the_sdk()
    {
        var (client, handler) = CreateClient();
        var request = new V2SalesforceRecordReadRequest(
            new V2SalesforceSemanticEntity("Account WHERE Name != null"));

        var error = await Assert.ThrowsAsync<SalesforceReadException>(
            () => client.ReadRecordsAsync(request, CancellationToken.None));

        Assert.Equal(SalesforceReadFailure.InvalidRequest, error.Failure);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("/query", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("Account%20WHERE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Related_aggregate_search_and_query_more_use_only_schema_resolved_provider_values()
    {
        var (client, handler) = CreateClient();
        var parent = new V2SalesforceResolvedRecord(
            new V2SalesforceSemanticEntity("Accounts"),
            "001000000000001");
        var related = await client.ReadRecordsAsync(
            new V2SalesforceRecordReadRequest(
                new V2SalesforceSemanticEntity("Contacts"),
                V2SalesforceRecordReadKind.Related,
                RelatedTo: parent),
            CancellationToken.None);
        var aggregate = await client.AggregateRecordsAsync(
            new V2SalesforceAggregateRequest(
                new V2SalesforceSemanticEntity("Accounts"),
                V2SemanticAggregateFunction.Sum,
                new V2SalesforceSemanticField("Annual Revenue"),
                new V2SalesforceSemanticField("Industry")),
            CancellationToken.None);
        await client.SearchRecordsAsync(
            new V2SalesforceSearchRequest(
                "Acme} OR FIND {*",
                [new V2SalesforceSemanticEntity("Accounts")],
                5),
            CancellationToken.None);
        var continued = await client.ContinueRecordsAsync(related.Continuation!, CancellationToken.None);

        var decoded = handler.Requests
            .Select(uri => Uri.UnescapeDataString(Uri.UnescapeDataString(uri.Query)).Replace('+', ' '))
            .ToArray();
        Assert.Contains(decoded, query => query.Contains("FROM Contact WHERE AccountId = '001000000000001' ORDER BY Id ASC", StringComparison.Ordinal));
        Assert.Contains(decoded, query => query.Contains("SUM(AnnualRevenue) value FROM Account GROUP BY Industry", StringComparison.Ordinal));
        var searchQuery = Assert.Single(decoded, query => query.Contains("FIND", StringComparison.Ordinal));
        Assert.Contains("Acme", searchQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("FIND {Acme} OR FIND {*}", searchQuery, StringComparison.Ordinal);
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath.EndsWith("/query/next-page", StringComparison.Ordinal));
        Assert.Equal(1, aggregate.ReturnedCount);
        Assert.Equal(1, continued.ReturnedCount);
    }

    [Fact]
    public async Task Discovery_returns_only_bounded_read_capabilities_without_api_names()
    {
        var (client, _) = CreateClient();

        var page = await client.DiscoverObjectsAsync(new V2SalesforceDiscoveryRequest(1), CancellationToken.None);
        using var json = JsonDocument.Parse(page.Content);

        var discovered = Assert.Single(json.RootElement.GetProperty("Objects").EnumerateArray());
        Assert.Equal("Account", discovered.GetProperty("Label").GetString());
        Assert.True(discovered.GetProperty("Queryable").GetBoolean());
        Assert.False(discovered.TryGetProperty("ApiName", out _));
    }

    private static (SalesforceApiClient Client, SemanticSalesforceHandler Handler) CreateClient()
    {
        var handler = new SemanticSalesforceHandler();
        var jsonHttp = new HttpClient(handler, disposeHandler: false);
        var xmlHttp = new HttpClient(handler, disposeHandler: false);
        var force = new ForceClient(
            "https://example.my.salesforce.com",
            "access-token",
            "v60.0",
            jsonHttp,
            xmlHttp,
            callerWillDisposeHttpClients: true);
        return (
            new SalesforceApiClient(force, "https://login.salesforce.com/id/00D000000000001/005000000000001"),
            handler);
    }

    private sealed class SemanticSalesforceHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Requests.Add(uri);
            var decoded = Uri.UnescapeDataString(uri.Query);
            var body = uri.AbsolutePath.TrimEnd('/') switch
            {
                var path when path.EndsWith("/sobjects", StringComparison.Ordinal) => GlobalDescribe,
                var path when path.EndsWith("/sobjects/Account/describe", StringComparison.Ordinal) => AccountDescribe,
                var path when path.EndsWith("/sobjects/Contact/describe", StringComparison.Ordinal) => ContactDescribe,
                var path when path.EndsWith("/query/next-page", StringComparison.Ordinal) => QueryPage(done: true),
                var path when path.EndsWith("/query", StringComparison.Ordinal) && decoded.Contains("SUM(", StringComparison.Ordinal) =>
                    """{"totalSize":1,"done":true,"records":[{"attributes":{"type":"AggregateResult"},"Industry":"Technology","value":1200}]}""",
                var path when path.EndsWith("/query", StringComparison.Ordinal) => QueryPage(done: false),
                var path when path.EndsWith("/search", StringComparison.Ordinal) =>
                    """[{"attributes":{"type":"Account"},"Id":"001000000000001","Name":"Acme"}]""",
                _ => throw new InvalidOperationException("Unexpected Salesforce request: " + uri)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string QueryPage(bool done) => $$"""
            {"totalSize":2,"done":{{done.ToString().ToLowerInvariant()}},"nextRecordsUrl":{{(done ? "null" : "\"/services/data/v60.0/query/next-page\"")}},"records":[{"attributes":{"type":"Account"},"Id":"001000000000001","Name":"Acme"}]}
            """;

        private const string GlobalDescribe = """
            {"encoding":"UTF-8","maxBatchSize":200,"sobjects":[
              {"name":"Account","label":"Account","labelPlural":"Accounts","queryable":true,"searchable":true,"keyPrefix":"001"},
              {"name":"Contact","label":"Contact","labelPlural":"Contacts","queryable":true,"searchable":true,"keyPrefix":"003"},
              {"name":"Secret__c","label":"Secret","labelPlural":"Secrets","queryable":false,"searchable":false,"keyPrefix":"a00"}
            ]}
            """;

        private const string AccountDescribe = """
            {"fields":[
              {"name":"Id","label":"Record ID","type":"id","filterable":true,"sortable":true,"groupable":true},
              {"name":"Name","label":"Account Name","type":"string","filterable":true,"sortable":true,"groupable":true,"nameField":true},
              {"name":"Industry","label":"Industry","type":"picklist","filterable":true,"sortable":true,"groupable":true},
              {"name":"AnnualRevenue","label":"Annual Revenue","type":"currency","filterable":true,"sortable":true,"groupable":false}
            ]}
            """;

        private const string ContactDescribe = """
            {"fields":[
              {"name":"Id","label":"Record ID","type":"id","filterable":true,"sortable":true,"groupable":true},
              {"name":"Name","label":"Full Name","type":"string","filterable":true,"sortable":true,"groupable":true,"nameField":true},
              {"name":"AccountId","label":"Account ID","type":"reference","filterable":true,"sortable":true,"groupable":true,"referenceTo":["Account"]}
            ]}
            """;
    }
}
