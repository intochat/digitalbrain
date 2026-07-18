using System.Net;
using System.Text;
using DigitalBrain.Integrations.Salesforce.Grains;
using DigitalBrain.Kernel.Runtime;
using Newtonsoft.Json.Linq;
using Salesforce.Force;
using Xunit;

namespace DigitalBrain.Integrations.Salesforce.Tests;

public sealed class SalesforceMutationApiClientTests
{
    private const string RecordId = "001000000000001";

    [Fact]
    public void Provider_exposes_the_typed_mutation_grain_contract()
    {
        Assert.True(typeof(ISalesforceMutationToolGrain).IsAssignableFrom(typeof(SalesforceMutationNeuron)));
    }

    [Fact]
    public async Task Preview_resolves_semantic_labels_and_returns_original_with_opaque_prepared_update()
    {
        var (client, _) = CreateClient("Original");

        var result = await client.PreviewUpdateAsync(Request("Desired"), CancellationToken.None);

        Assert.Equal(SalesforceMutationStatus.Prepared, result.Status);
        Assert.Equal("Original", result.OriginalValue);
        Assert.Equal("Desired", result.CanonicalDesiredValue);
        Assert.Equal("Account", result.ResolvedEntityLabel);
        Assert.Equal("Account Name", result.ResolvedFieldLabel);
        Assert.NotNull(result.PreparedUpdate);
        Assert.NotEmpty(result.PreparedUpdate.Payload);
    }

    [Fact]
    public async Task Preview_returns_the_exact_canonical_value_that_the_prepared_update_will_apply()
    {
        var (client, _) = CreateClient("1", fieldType: "int");

        var result = await client.PreviewUpdateAsync(Request("01"), CancellationToken.None);

        Assert.Equal(SalesforceMutationStatus.Prepared, result.Status);
        Assert.Equal("1", result.CanonicalDesiredValue);
    }

    [Fact]
    public async Task Apply_updates_exactly_one_resolved_field_and_verifies_the_result()
    {
        var (client, handler) = CreateClient("Original");
        var preview = await client.PreviewUpdateAsync(Request("Desired"), CancellationToken.None);

        var result = await client.ApplyUpdateAsync(preview.PreparedUpdate!, CancellationToken.None);

        Assert.Equal(SalesforceMutationStatus.Applied, result.Status);
        var patch = Assert.Single(handler.Patches);
        Assert.Equal("/services/data/v60.0/sobjects/Account/" + RecordId, patch.Path);
        var property = Assert.Single(JObject.Parse(patch.Body).Properties());
        Assert.Equal("Name", property.Name);
        Assert.Equal("Desired", property.Value.Value<string>());
    }

    [Fact]
    public async Task Apply_reconciles_an_already_desired_value_without_updating()
    {
        var (client, handler) = CreateClient("Desired");
        var preview = await client.PreviewUpdateAsync(Request("Desired"), CancellationToken.None);

        var result = await client.ApplyUpdateAsync(preview.PreparedUpdate!, CancellationToken.None);

        Assert.Equal(SalesforceMutationStatus.AlreadyApplied, result.Status);
        Assert.Empty(handler.Patches);
    }

    [Fact]
    public async Task Apply_rejects_an_original_value_conflict_without_updating()
    {
        var (client, handler) = CreateClient("Original");
        var preview = await client.PreviewUpdateAsync(Request("Desired"), CancellationToken.None);
        handler.CurrentValue = "Changed elsewhere";

        var result = await client.ApplyUpdateAsync(preview.PreparedUpdate!, CancellationToken.None);

        Assert.Equal(SalesforceMutationStatus.Conflict, result.Status);
        Assert.Empty(handler.Patches);
    }

    [Fact]
    public async Task Apply_reports_verification_failure_when_the_provider_does_not_persist_the_value()
    {
        var (client, handler) = CreateClient("Original");
        handler.PersistUpdates = false;
        var preview = await client.PreviewUpdateAsync(Request("Desired"), CancellationToken.None);

        var result = await client.ApplyUpdateAsync(preview.PreparedUpdate!, CancellationToken.None);

        Assert.Equal(SalesforceMutationStatus.VerificationFailed, result.Status);
        Assert.Single(handler.Patches);
    }

    [Fact]
    public async Task Verify_reads_the_provider_after_apply_and_confirms_the_approved_value()
    {
        var (client, _) = CreateClient("Original");
        var preview = await client.PreviewUpdateAsync(Request("Desired"), CancellationToken.None);
        var apply = await client.ApplyUpdateAsync(preview.PreparedUpdate!, CancellationToken.None);

        var verification = await client.VerifyUpdateAsync(preview.PreparedUpdate!, CancellationToken.None);

        Assert.Equal(SalesforceMutationStatus.Applied, apply.Status);
        Assert.True(verification.Verified);
        Assert.Null(verification.SafeReason);
    }

    private static SalesforceUpdatePreviewRequest Request(string newValue) =>
        new(new SalesforceSemanticEntity("Accounts"), RecordId, new SalesforceSemanticField("Account Name"), newValue);

    private static (SalesforceApiClient Client, MutationSalesforceHandler Handler) CreateClient(
        string currentValue,
        string fieldType = "string")
    {
        var handler = new MutationSalesforceHandler(currentValue, fieldType);
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
            new SalesforceApiClient(force),
            handler);
    }

    private sealed class MutationSalesforceHandler(string currentValue, string fieldType) : HttpMessageHandler
    {
        public string CurrentValue { get; set; } = currentValue;
        public bool PersistUpdates { get; set; } = true;
        public List<(string Path, string Body)> Patches { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath.TrimEnd('/');
            if (request.Method == HttpMethod.Patch)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Patches.Add((path, body));
                if (PersistUpdates)
                    CurrentValue = JObject.Parse(body).Value<string>("Name")!;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            var response = path switch
            {
                var value when value.EndsWith("/sobjects", StringComparison.Ordinal) => GlobalDescribe,
                var value when value.EndsWith("/sobjects/Account/describe", StringComparison.Ordinal) => AccountDescribe,
                var value when value.EndsWith("/query", StringComparison.Ordinal) => QueryResult(),
                _ => throw new InvalidOperationException("Unexpected Salesforce request: " + uri)
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }

        private string QueryResult() => $$"""
            {"totalSize":1,"done":true,"records":[{"attributes":{"type":"Account"},"Id":"{{RecordId}}","Name":{{JToken.FromObject(CurrentValue).ToString(Newtonsoft.Json.Formatting.None)}}}]}
            """;

        private const string GlobalDescribe = """
            {"encoding":"UTF-8","maxBatchSize":200,"sobjects":[
              {"name":"Account","label":"Account","labelPlural":"Accounts","queryable":true,"searchable":true,"updateable":true,"keyPrefix":"001"}
            ]}
            """;

        private string AccountDescribe => $$"""
            {"fields":[
              {"name":"Id","label":"Record ID","type":"id","filterable":true,"sortable":true,"groupable":true,"updateable":false},
              {"name":"Name","label":"Account Name","type":"{{fieldType}}","filterable":true,"sortable":true,"groupable":true,"nameField":true,"updateable":true}
            ]}
            """;
    }
}
