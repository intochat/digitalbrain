using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;

namespace DigitalBrain.Deployment.Tests;

// Echoes inputs as outputs, answers the ARM lookups DeploymentKit makes, and records every resource.
internal sealed class AzureMocks : IMocks
{
    public ConcurrentBag<MockResourceArgs> Resources { get; } = [];

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        Resources.Add(args);
        var state = args.Inputs
            .SetItem("name", args.Inputs.TryGetValue("name", out var name) ? name : args.Name ?? "")
            .SetItem("defaultDomain", "calm-sea.westeurope.azurecontainerapps.io")
            .SetItem("principalId", "principal-" + args.Name)
            .SetItem("clientId", "client-" + args.Name)
            .SetItem("poolManagementEndpoint", $"https://{args.Name}.westeurope.azurecontainerapps.io")
            .SetItem("b64Std", "c2VjcmV0LWtleS1ieXRlcw==")
            .SetItem("fullyQualifiedDomainName", args.Name + ".postgres.database.azure.com");
        if (args.Type == "azure-native:keyvault:Secret")
        {
            state = state.SetItem("properties", ImmutableDictionary<string, object>.Empty.Add("secretUri", $"https://vault.vault.azure.net/secrets/{args.Name}"));
        }
        return Task.FromResult<(string?, object)>(($"/resources/{args.Name}", state));
    }

    public Task<object> CallAsync(MockCallArgs args) => Task.FromResult<object>(ImmutableDictionary<string, object>.Empty
        .Add("subscriptionId", "00000000-0000-0000-0000-000000000000").Add("tenantId", "11111111-1111-1111-1111-111111111111")
        .Add("objectId", "22222222-2222-2222-2222-222222222222").Add("clientId", "33333333-3333-3333-3333-333333333333")
        .Add("keys", ImmutableArray.Create<object>(
            ImmutableDictionary<string, object>.Empty.Add("keyName", "key1").Add("value", "key-one").Add("permissions", "FULL"),
            ImmutableDictionary<string, object>.Empty.Add("keyName", "key2").Add("value", "key-two").Add("permissions", "FULL")))
        .Add("username", "registry").Add("passwords", ImmutableArray.Create<object>(ImmutableDictionary<string, object>.Empty.Add("name", "password").Add("value", "secret"))));

    public MockResourceArgs Single(string type, string name) => Resources.Single(resource => resource.Type == type && resource.Name == name);

    // Stack config the way the Pulumi engine hands it to a program, parameters under "digitalbrain:".
    public static IDisposable Config(IReadOnlyDictionary<string, string> values)
    {
        System.Environment.SetEnvironmentVariable("AZURE_TENANT_ID", "11111111-1111-1111-1111-111111111111");
        System.Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", "33333333-3333-3333-3333-333333333333");
        System.Environment.SetEnvironmentVariable("AZURE_SUBSCRIPTION_ID", "00000000-0000-0000-0000-000000000000");
        System.Environment.SetEnvironmentVariable("PULUMI_CONFIG", System.Text.Json.JsonSerializer.Serialize(
            values.ToDictionary(value => "digitalbrain:" + value.Key, value => value.Value)));
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => System.Environment.SetEnvironmentVariable("PULUMI_CONFIG", null);
    }
}
