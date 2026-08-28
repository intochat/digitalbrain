using System.Text.Json;

namespace DigitalBrain.Integrations.Fakes;

internal sealed class SalesforceFakeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, JsonElement>> _accounts = new(StringComparer.Ordinal)
    {
        ["001INTOCHAT"] = Account(
            "001INTOCHAT", "IntoChat", "https://intochat.io",
            "Verified customer conversation platform.", descriptionVerified: true),
        ["001ACME"] = Account(
            "001ACME", "Acme", "https://acme.test",
            "Verified test company.", descriptionVerified: true),
    };

    public JsonElement Query(string query)
    {
        lock (_gate)
        {
            var id = query.Contains("001ACME", StringComparison.OrdinalIgnoreCase)
                || query.Contains("acme", StringComparison.OrdinalIgnoreCase)
                    ? "001ACME"
                    : "001INTOCHAT";
            return JsonSerializer.SerializeToElement(new
            {
                totalSize = 1,
                done = true,
                records = new[] { _accounts[id] },
            });
        }
    }

    public JsonElement Create(JsonElement body, string objectName)
    {
        lock (_gate)
        {
            var id = $"001CREATED{_accounts.Count + 1}";
            var account = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["Id"] = JsonSerializer.SerializeToElement(id),
            };
            Merge(account, body);
            _accounts[id] = account;
            return Result(id, objectName, body, created: true);
        }
    }

    public JsonElement Update(string id, JsonElement body, string objectName)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(id, out var account))
            {
                account = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["Id"] = JsonSerializer.SerializeToElement(id),
                };
                _accounts[id] = account;
            }
            Merge(account, body);
            return Result(id, objectName, body, created: false);
        }
    }

    private static Dictionary<string, JsonElement> Account(
        string id,
        string name,
        string website,
        string description,
        bool descriptionVerified)
        => new(StringComparer.Ordinal)
        {
            ["Id"] = JsonSerializer.SerializeToElement(id),
            ["Name"] = JsonSerializer.SerializeToElement(name),
            ["Website"] = JsonSerializer.SerializeToElement(website),
            ["Description"] = JsonSerializer.SerializeToElement(description),
            ["DescriptionVerified"] = JsonSerializer.SerializeToElement(descriptionVerified),
        };

    private static void Merge(Dictionary<string, JsonElement> target, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (var property in body.EnumerateObject())
        {
            target[property.Name] = property.Value.Clone();
        }
    }

    private static JsonElement Result(string id, string objectName, JsonElement body, bool created)
        => JsonSerializer.SerializeToElement(new
        {
            id,
            success = true,
            created,
            sobjectName = objectName,
            body,
        });
}
