using System.Collections.Immutable;
using DeploymentKit.Enums;
using DigitalBrain.Deployment;
using Pulumi;
using Pulumi.Testing;

namespace DigitalBrain.Tests;

// Pulumi runs one deployment per process at a time.
[Collection(nameof(DigitalBrainDeploymentFacts))]
public sealed class DigitalBrainDeploymentFacts
{
    private const string Manifest = """
        {
          "resources": {
            "storage": { "type": "azure.bicep.v0", "path": "storage.module.bicep" },
            "clustering": { "type": "value.v0", "connectionString": "{storage.outputs.tableEndpoint}" },
            "api-key": { "type": "parameter.v0", "value": "{api-key.inputs.value}", "inputs": { "value": { "type": "string", "secret": true } } },
            "client-id": { "type": "parameter.v0", "value": "{client-id.inputs.value}", "inputs": { "value": { "type": "string" } } },
            "vectors": { "type": "container.v0", "image": "docker.io/qdrant/qdrant:v1", "connectionString": "Endpoint={vectors.bindings.grpc.url};Key={api-key.value}" },
            "brain": {
              "type": "project.v0",
              "env": {
                "DigitalBrain__Modules__0": "Some.Module, Some.Assembly",
                "HTTP_PORTS": "{brain.bindings.http.targetPort}",
                "PublicOrigin": "{brain.bindings.http.url}",
                "ConnectionStrings__clustering": "{clustering.connectionString}",
                "ConnectionStrings__vectors": "{vectors.connectionString}",
                "Provider__ApiKey": "{api-key.value}",
                "Provider__ClientId": "{client-id.value}"
              }
            }
          }
        }
        """;

    [Fact]
    public async Task SecretsGoToKeyVaultAndReachTheRuntimeAsVaultReferences()
    {
        var mocks = new AzureMocks();
        using (AzureMocks.Config(new Dictionary<string, string> { ["api-key"] = "sk-123", ["client-id"] = "client-abc" }))
        {
            await Deploy(mocks, [new VectorsDeployment()]);
        }

        var runtime = mocks.Single("azure-native:app:ContainerApp", "dbrain-brain");
        var variables = Environment(runtime);
        Assert.Equal("client-abc", variables["Provider__ClientId"].Value);
        Assert.Equal("8080", variables["HTTP_PORTS"].Value);
        Assert.Equal("https://dbrain-brain.calm-sea.westeurope.azurecontainerapps.io", variables["PublicOrigin"].Value);
        Assert.Matches(@"^https://.+\.table\.core\.windows\.net/$", variables["ConnectionStrings__clustering"].Value);
        Assert.Equal("provider--apikey", variables["Provider__ApiKey"].SecretRef);
        Assert.Equal("connectionstrings--vectors", variables["ConnectionStrings__vectors"].SecretRef);
        var vaulted = mocks.Resources.Where(resource => resource.Type == "azure-native:keyvault:Secret").Select(resource => resource.Name).Order().ToArray();
        Assert.Equal(["dbrain-connectionstrings--vectors", "dbrain-provider--apikey"], vaulted);
    }

    [Fact]
    public async Task WhatNoModuleProvidesFailsTheDeploymentByName()
    {
        var mocks = new AzureMocks();
        using var config = AzureMocks.Config(new Dictionary<string, string> { ["api-key"] = "sk-123", ["client-id"] = "client-abc" });

        var error = await Assert.ThrowsAsync<Pulumi.RunException>(() => Deploy(mocks, []));

        Assert.Contains("vectors.bindings.grpc.url", error.ToString(), StringComparison.Ordinal);
    }

    private static Task<ImmutableArray<Resource>> Deploy(AzureMocks mocks, IReadOnlyList<IDigitalBrainModuleDeployment> modules)
        => Pulumi.Deployment.TestAsync(mocks, new TestOptions { IsPreview = false, ProjectName = "digitalbrain", StackName = "test" }, async () =>
        {
            await DigitalBrainDeployment.RunAsync(new DigitalBrainDeploymentOptions
            {
                Manifest = AspireManifest.Parse(Manifest),
                Parameters = new Config("digitalbrain"),
                SubscriptionId = "00000000-0000-0000-0000-000000000000",
                RuntimeImage = "docker.io/digitalbrain/kernel:test",
                Validation = ValidationMode.Skip,
                Modules = modules,
            });
        });

    private static Dictionary<string, (string? Value, string? SecretRef)> Environment(Pulumi.Testing.MockResourceArgs app)
    {
        var template = (ImmutableDictionary<string, object>)app.Inputs["template"];
        var container = (ImmutableDictionary<string, object>)((ImmutableArray<object>)template["containers"])[0];
        return ((ImmutableArray<object>)container["env"]).Cast<ImmutableDictionary<string, object>>()
            .ToDictionary(variable => (string)variable["name"],
                variable => (variable.TryGetValue("value", out var value) ? value as string : null, variable.TryGetValue("secretRef", out var secret) ? secret as string : null));
    }

    private sealed class VectorsDeployment : IDigitalBrainModuleDeployment
    {
        public void Deploy(ModuleDeploymentContext context) => context.Provide("vectors", "bindings.grpc.url", "https://vectors.internal");
    }
}
