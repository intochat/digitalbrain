using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salesforce.Common.Models.Json;
using Salesforce.Force;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceApiClient(ForceClient client, string? identityUrl = null) : ISalesforceApiClient
{
    public async Task<string> GetCurrentUserProfileAsync(CancellationToken ct)
    {
        if (!IsAllowedIdentityUrl(identityUrl))
        {
            throw new InvalidOperationException(
                "Salesforce identity information is unavailable. Reconnect Salesforce to continue.");
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            var profile = await client.UserInfo<UserInfo>(identityUrl).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return JsonConvert.SerializeObject(new
            {
                profile.UserId,
                profile.OrganizationId,
                profile.DisplayName,
                profile.Username,
                profile.Email,
                profile.UserType,
                profile.Active,
                profile.Locale,
                profile.Language
            });
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw new InvalidOperationException($"Salesforce profile read failed: {ex.Message}", ex);
        }
    }

    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct)
    {
        var limit = Math.Clamp(maxResults, 1, 50);
        return QueryAsync(
            $"SELECT Id, Name, Type, Industry, Website, BillingCity, BillingCountry, LastModifiedDate FROM Account ORDER BY LastModifiedDate DESC LIMIT {limit}",
            ct);
    }

    public Task<string[]> ListContactsAsync(int maxResults, CancellationToken ct)
    {
        var limit = Math.Clamp(maxResults, 1, 50);
        return QueryAsync(
            $"SELECT Id, Name, Title, Email, Phone, Account.Name, LastModifiedDate FROM Contact ORDER BY LastModifiedDate DESC LIMIT {limit}",
            ct);
    }

    public async Task<string> DescribeCrmAccessAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var account = await client.DescribeAsync<JObject>("Account").ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var contact = await client.DescribeAsync<JObject>("Contact").ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            return JsonConvert.SerializeObject(new
            {
                Account = SummarizeDescribe(account),
                Contact = SummarizeDescribe(contact)
            });
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw new InvalidOperationException($"Salesforce metadata read failed: {ex.Message}", ex);
        }
    }

    private async Task<string[]> QueryAsync(string soql, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(soql))
        {
            throw new ArgumentException("SOQL query is required.", nameof(soql));
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            var result = await client.QueryAsync<Dictionary<string, object?>>(soql).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            return result.Records
                .Select(record => JsonConvert.SerializeObject(Normalize(record)))
                .ToArray();
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw new InvalidOperationException($"Salesforce query failed: {ex.Message}");
        }
    }

    private static object SummarizeDescribe(JObject describe) => new
    {
        Name = describe.Value<string>("name"),
        Label = describe.Value<string>("label"),
        Queryable = describe.Value<bool?>("queryable") ?? false,
        Searchable = describe.Value<bool?>("searchable") ?? false,
        AccessibleFields = (describe["fields"] as JArray ?? [])
            .OfType<JObject>()
            .Select(field => new
            {
                Name = field.Value<string>("name"),
                Label = field.Value<string>("label"),
                Type = field.Value<string>("type")
            })
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToArray()
    };

    private static bool IsAllowedIdentityUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "login.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "test.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".my.salesforce.com", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.StartsWith("/id/", StringComparison.Ordinal);

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

    private static bool IsSalesforceClientException(Exception ex) =>
        ex is not OperationCanceledException &&
        ex.GetType().Namespace?.StartsWith("Salesforce.", StringComparison.Ordinal) == true;
}
