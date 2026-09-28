using System.Collections.Immutable;
using DeploymentKit.Enums;
using DigitalBrain.Deployment;
using DigitalBrain.Tests;
using Pulumi;
using Pulumi.Testing;

namespace IntoChat.Tests;

// The hosted AppHost manifest (Fixtures/hosted-manifest.json, regenerated with
// `dotnet run --project src/Applications/IntoChat/AppHost -- --publisher manifest --output-path <file> IntoChat:Profile=hosted`)
// must deploy with nothing left unresolved: every module that runs something deploys it, every secret
// reaches Key Vault.
// Pulumi runs one deployment per process at a time, and stack config is process-wide.
[Collection(PulumiDeployments.Name)]
public sealed class HostedDeploymentFacts
{
    private static readonly Dictionary<string, string> Parameters = new()
    {
        ["openai-api-key"] = "sk-openai", ["tavily-api-key"] = "tvly", ["supabase-connection"] = "Host=db.supabase.co;Password=pw",
        ["gmail-client-id"] = "gmail-client", ["gmail-client-secret"] = "gmail-secret",
        ["salesforce-consumer-key"] = "sf-key", ["salesforce-consumer-secret"] = "sf-secret",
        ["compute-postgres-password"] = "Postgres-Password-1", ["qdrant-Key"] = "qdrant-key", ["ClickHouse-password"] = "clickhouse-pw",
    };

    [Fact]
    public async Task EveryModuleDeploysWhatItRunsAndEverySecretIsVaulted()
    {
        var mocks = new AzureMocks();
        using (AzureMocks.Config(Parameters))
        {
            await Pulumi.Deployment.TestAsync(mocks, new TestOptions { IsPreview = false, ProjectName = "intochat", StackName = "test" }, async () =>
            {
                await DigitalBrainDeployment.RunAsync(new DigitalBrainDeploymentOptions
                {
                    Manifest = AspireManifest.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "hosted-manifest.json")),
                    Parameters = new Config("digitalbrain"),
                    SubscriptionId = "00000000-0000-0000-0000-000000000000",
                    RuntimeImage = "docker.io/vhorbachov/digitalbrain-kernel:test",
                    NamingPrefix = "intochat",
                    Name = "intochat",
                    Validation = ValidationMode.Skip,
                });
            });
        }

        var apps = mocks.Resources.Where(resource => resource.Type == "azure-native:app:ContainerApp").Select(resource => resource.Name).Order().ToArray();
        Assert.Equal(["intochat-brain", "intochat-clickhouse", "intochat-ollama", "intochat-qdrant"], apps);
        Assert.Single(mocks.Resources, resource => resource.Type == "azure-native:dbforpostgresql:Server");

        var vaulted = mocks.Resources.Where(resource => resource.Type == "azure-native:keyvault:Secret").Select(resource => resource.Name).ToHashSet();
        foreach (var variable in new[]
        {
            "digitalbrain--ai--openai--apikey", "digitalbrain--ai--tavily--apikey", "connectionstrings--supabase",
            "digitalbrain--google--gmail--oauth--clientsecret", "digitalbrain--salesforce--oauth--consumersecret",
            "connectionstrings--compute", "connectionstrings--qdrant", "connectionstrings--clickhouse",
        })
        {
            Assert.Contains("intochat-" + variable, vaulted);
        }

        var runtime = Environment(mocks.Resources.Single(resource => resource.Name == "intochat-brain" && resource.Type == "azure-native:app:ContainerApp"));
        Assert.Equal("gmail-client", runtime["DigitalBrain__Google__Gmail__OAuth__ClientId"]);
        Assert.Equal("intochat", runtime["Orleans__ClusterId"]);
        Assert.StartsWith("https://intochat-brain.", runtime["DigitalBrain__Google__Gmail__OAuth__PublicOrigin"], StringComparison.Ordinal);
        Assert.DoesNotContain(runtime.Values, value => value?.Contains('{', StringComparison.Ordinal) == true);
        // The manifest's COMPUTE_* describe the local container; the cloud values describe the Flexible Server.
        Assert.NotEqual("compute", runtime["COMPUTE_DATABASENAME"]);
        Assert.Null(runtime["COMPUTE_URI"]);
        Assert.Contains("intochat-compute-uri", vaulted);

        var ollama = (ImmutableArray<object>)((ImmutableDictionary<string, object>)((ImmutableArray<object>)((ImmutableDictionary<string, object>)mocks.Resources
            .Single(resource => resource.Name == "intochat-ollama" && resource.Type == "azure-native:app:ContainerApp").Inputs["template"])["containers"])[0])["args"];
        Assert.Contains("ollama pull gemma4:e2b", (string)ollama[0], StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Environment(MockResourceArgs app)
    {
        var template = (ImmutableDictionary<string, object>)app.Inputs["template"];
        var container = (ImmutableDictionary<string, object>)((ImmutableArray<object>)template["containers"])[0];
        return ((ImmutableArray<object>)container["env"]).Cast<ImmutableDictionary<string, object>>()
            .ToDictionary(variable => (string)variable["name"], variable => variable.TryGetValue("value", out var value) ? value as string : null);
    }
}
