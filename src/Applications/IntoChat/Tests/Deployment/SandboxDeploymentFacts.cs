using System.Collections.Immutable;
using DeploymentKit.Enums;
using DigitalBrain.Deployment;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Tests;
using Pulumi;
using Pulumi.Testing;

namespace IntoChat.Tests;

// Pulumi runs one deployment per process at a time, and stack config is process-wide.
[Collection(PulumiDeployments.Name)]
public sealed class SandboxDeploymentFacts
{
    private const string Manifest = """
        {
          "resources": {
            "brain": { "type": "project.v0", "env": { "DigitalBrain__Modules__0": "DigitalBrain.Microsoft.CSharp.CSharpModule, DigitalBrain.Modules.Microsoft.CSharp" } }
          }
        }
        """;

    [Fact]
    public async Task ScriptsRunInAnIsolatedSessionPoolTheRuntimeMayCall()
    {
        var mocks = new AzureMocks();
        using (AzureMocks.Config(new Dictionary<string, string>
        {
            ["sandboxImage"] = "intochat.azurecr.io/csharp-sandbox:test",
            ["sessionExecutorRoleId"] = "executor-role",
        }))
        {
            await Pulumi.Deployment.TestAsync(mocks, new TestOptions { IsPreview = false, ProjectName = "intochat", StackName = "test" }, async () =>
            {
                await DigitalBrainDeployment.RunAsync(new DigitalBrainDeploymentOptions
                {
                    Manifest = AspireManifest.Parse(Manifest),
                    Parameters = new Config("digitalbrain"),
                    SubscriptionId = "00000000-0000-0000-0000-000000000000",
                    RuntimeImage = "docker.io/vhorbachov/digitalbrain-kernel:test",
                    NamingPrefix = "intochat",
                    Validation = ValidationMode.Skip,
                });
            });
        }

        var pool = mocks.Single("azure-native:app:ContainerAppsSessionPool", "intochat-sandbox");
        Assert.Equal("CustomContainer", pool.Inputs["containerType"]);
        Assert.Equal("/resources/intochat-sandbox", pool.Inputs["environmentId"]);
        var rules = (ImmutableArray<object>)mocks.Single("azure-native:network:NetworkSecurityGroup", "intochat-sandbox").Inputs["securityRules"];
        Assert.Equal(["allow-https-out", "deny-other-out"], rules.Cast<ImmutableDictionary<string, object>>().Select(rule => (string)rule["name"]));
        var executor = mocks.Single("azure-native:authorization:RoleAssignment", "intochat-sandbox-sessions");
        Assert.EndsWith("/roleDefinitions/executor-role", (string)executor.Inputs["roleDefinitionId"], StringComparison.Ordinal);

        var runtime = Environment(mocks.Single("azure-native:app:ContainerApp", "intochat-brain"));
        Assert.Equal("https://intochat-sandbox.westeurope.azurecontainerapps.io", runtime["DigitalBrain__CSharp__SessionPoolEndpoint"].Value);
        Assert.StartsWith("https://intochat-brain.", runtime["DigitalBrain__CSharp__EdgeUrl"].Value, StringComparison.Ordinal);
        Assert.Equal("digitalbrain--csharp--runtokenkey", runtime["DigitalBrain__CSharp__RunTokenKey"].SecretRef);
        Assert.Equal(CSharpSandbox.Port, Convert.ToInt32(((ImmutableDictionary<string, object>)((ImmutableDictionary<string, object>)pool.Inputs["customContainerTemplate"])["ingress"])["targetPort"], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, (string? Value, string? SecretRef)> Environment(MockResourceArgs app)
    {
        var template = (ImmutableDictionary<string, object>)app.Inputs["template"];
        var container = (ImmutableDictionary<string, object>)((ImmutableArray<object>)template["containers"])[0];
        return ((ImmutableArray<object>)container["env"]).Cast<ImmutableDictionary<string, object>>()
            .ToDictionary(variable => (string)variable["name"],
                variable => (variable.TryGetValue("value", out var value) ? value as string : null, variable.TryGetValue("secretRef", out var secret) ? secret as string : null));
    }
}
