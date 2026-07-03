using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salesforce.Force;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceApiClient(ForceClient client) : ISalesforceApiClient
{
    public async Task<string[]> QueryAsync(string soql, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(soql))
            throw new ArgumentException("SOQL query is required.", nameof(soql));

        ct.ThrowIfCancellationRequested();
        var result = await client.QueryAsync<Dictionary<string, object?>>(soql).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return result.Records
            .Select(record => JsonConvert.SerializeObject(Normalize(record)))
            .ToArray();
    }

    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct)
    {
        var limit = Math.Clamp(maxResults, 1, 200);
        return QueryAsync(
            $"SELECT Id, Name, Type, Industry, Website FROM Account ORDER BY LastModifiedDate DESC LIMIT {limit}",
            ct);
    }

    private static object? Normalize(object? value) => value switch
    {
        JValue jValue => jValue.Value,
        JObject jObject => jObject.Properties()
            .ToDictionary(property => property.Name, property => Normalize(property.Value)),
        JArray jArray => jArray.Select(Normalize).ToArray(),
        IDictionary<string, object?> dictionary => dictionary
            .Where(kv => !string.Equals(kv.Key, "attributes", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => Normalize(kv.Value)),
        _ => value
    };
}
