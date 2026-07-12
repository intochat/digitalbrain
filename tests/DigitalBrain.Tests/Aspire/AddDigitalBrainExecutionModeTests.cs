using System.Reflection;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Aspire;

namespace DigitalBrain.Tests.Aspire;

// Real, executed coverage for the builder.ExecutionContext.IsRunMode branching added to
// AddDigitalBrain (storage emulator + Ollama container vs. AddConnectionString("llm")
// placeholder). Uses DistributedApplicationTestingBuilder.CreateAsync against the real
// DigitalBrain.AppHost entry point.
// but deliberately stops after CreateAsync — never calling BuildAsync/StartAsync — so it only
// inspects the declared resource graph. No Docker, no Orleans, no container start.
public sealed class AddDigitalBrainExecutionModeTests
{
    private static async Task<IDistributedApplicationTestingBuilder> CreateAppHostBuilderAsync(params string[] args)
    {
        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, "DigitalBrain.AppHost.dll")),
            "DigitalBrain.AppHost must be built and copied for AppHost model tests.");

        var appHostAssembly = Assembly.Load("DigitalBrain.AppHost");
        var programType = appHostAssembly.GetTypes().FirstOrDefault(t => t.Name == "Program")
                          ?? appHostAssembly.EntryPoint?.DeclaringType
                          ?? throw new InvalidOperationException("Could not locate AppHost Program type for DistributedApplicationTestingBuilder.");

        return await DistributedApplicationTestingBuilder.CreateAsync(programType, args);
    }

    [Fact]
    public async Task RunMode_CreatesEmulatedStorageAndOllamaContainer()
    {
        // Default args == run mode, matching every `dotnet run`/`aspire run` invocation today.
        var builder = await CreateAppHostBuilderAsync();

        Assert.True(builder.ExecutionContext.IsRunMode);

        var storage = Assert.Single(builder.Resources, r => r.Name == "runtime-storage");
        Assert.Contains(storage.Annotations, a => a.GetType().Name == "EmulatorResourceAnnotation");
        var dataVolume = Assert.Single(
            builder.Resources.SelectMany(resource => resource.Annotations.OfType<ContainerMountAnnotation>()),
            mount => mount.Target == "/data");
        Assert.Equal(ContainerMountType.Volume, dataVolume.Type);
        Assert.Equal("digitalbrain-main-azurite-data", dataVolume.Source);
        Assert.False(dataVolume.IsReadOnly);

        Assert.Contains(builder.Resources, r => r.Name == "conversationstate" && r.GetType().Name == "AzureBlobStorageResource");
        Assert.Contains(builder.Resources, r => r.Name == "surfacefeedstate" && r.GetType().Name == "AzureBlobStorageResource");
        Assert.Contains(builder.Resources, r => r.Name == "sessionstate" && r.GetType().Name == "AzureBlobStorageResource");

        Assert.Contains(builder.Resources, r => r.Name == "ollama" && r.GetType().Name == "OllamaResource");

        var llm = Assert.Single(builder.Resources, r => r.Name == "llm");
        Assert.Equal("OllamaModelResource", llm.GetType().Name);

        var embed = Assert.Single(builder.Resources, r => r.Name == "embed");
        Assert.Equal("OllamaModelResource", embed.GetType().Name);

        // Development run mode automatically starts the authenticated Flutter shell.
        Assert.Contains(builder.Resources, r => r.Name == "flutter-ui");
    }

    [Fact]
    public async Task TestProfile_DeclaresFlutterClientWiredOnlyToTransport()
    {
        const string salesforceCallback = "https://brain.example/oauth/callback/salesforce";
        var builder = await CreateAppHostBuilderAsync(
            "--DigitalBrain:Profile=Test",
            $"--Parameters:salesforce-redirect-uri={salesforceCallback}");

        Assert.Equal("Test", builder.Configuration["DigitalBrain:Profile"]);
        Assert.Contains(builder.Resources, r => r.Name == "kernel");

        var mcp = Assert.Single(builder.Resources, r => r.Name == "mcp");
        var flutter = Assert.Single(builder.Resources, r => r.Name == "flutter-ui");
        var bootstrapSecret = Assert.IsType<ParameterResource>(
            Assert.Single(builder.Resources, r => r.Name == "runtime-ui-bootstrap-secret"));
        Assert.True(bootstrapSecret.Secret);

        var httpsEndpoint = Assert.Single(
            mcp.Annotations.OfType<EndpointAnnotation>(),
            endpoint => endpoint.Name == "https");
        Assert.Equal("https", httpsEndpoint.UriScheme);
        Assert.Equal("http2", httpsEndpoint.Transport);
        Assert.True(httpsEndpoint.IsProxied);
        Assert.Contains(
            mcp.Annotations.OfType<HealthCheckAnnotation>(),
            annotation => annotation.Key == "mcp_https_/health_200_check");

        var wait = Assert.Single(
            flutter.Annotations.OfType<WaitAnnotation>(),
            annotation => ReferenceEquals(annotation.Resource, mcp));
        Assert.Equal(WaitType.WaitUntilHealthy, wait.WaitType);

        var transportReference = Assert.Single(flutter.Annotations.OfType<EndpointReferenceAnnotation>());
        Assert.Same(mcp, transportReference.Resource);
        Assert.False(transportReference.UseAllEndpoints);
        Assert.Equal(["https"], transportReference.EndpointNames);

        var flutterEnvironment = await EvaluateEnvironmentAsync(builder, flutter);
        Assert.DoesNotContain("DIGITALBRAIN_RUNTIME", flutterEnvironment.Keys);
        var endpointReference = Assert.IsType<EndpointReference>(
            flutterEnvironment[FlutterAspireExtensions.TransportEndpointEnvironmentVariable]);
        Assert.Same(mcp, endpointReference.Resource);
        Assert.Equal("https", endpointReference.EndpointName);
        Assert.Same(
            bootstrapSecret,
            flutterEnvironment[FlutterAspireExtensions.BootstrapSecretEnvironmentVariable]);
        Assert.DoesNotContain("DIGITALBRAIN_SALESFORCE_OAUTH_CALLBACK", flutterEnvironment.Keys);

        var mcpEnvironment = await EvaluateEnvironmentAsync(builder, mcp);
        Assert.Same(bootstrapSecret, mcpEnvironment["DigitalBrain__Runtime__Ui__BootstrapSecret"]);
        Assert.DoesNotContain("DigitalBrain__Runtime__StorageNamespace", mcpEnvironment.Keys);
        Assert.DoesNotContain(
            mcpEnvironment,
            pair => pair.Key.Contains("StorePath", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Contains("LocalApplicationData", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Contains("__V2__", StringComparison.OrdinalIgnoreCase));

        var durableBlobNames = new[] { "conversationstate", "surfacefeedstate", "sessionstate" };
        foreach (var blobName in durableBlobNames)
        {
            var blob = Assert.Single(builder.Resources, resource => resource.Name == blobName);
            Assert.Contains(
                mcp.Annotations.OfType<ResourceRelationshipAnnotation>(),
                relationship => ReferenceEquals(relationship.Resource, blob));
            Assert.Contains(
                mcp.Annotations.OfType<WaitAnnotation>(),
                waitAnnotation => ReferenceEquals(waitAnnotation.Resource, blob) &&
                                  waitAnnotation.WaitType == WaitType.WaitUntilHealthy);
        }

        var relationships = flutter.Annotations.OfType<ResourceRelationshipAnnotation>().ToArray();
        Assert.Contains(relationships, relationship => ReferenceEquals(relationship.Resource, mcp));
        Assert.Contains(relationships, relationship => ReferenceEquals(relationship.Resource, bootstrapSecret));
        Assert.All(
            relationships,
            relationship => Assert.True(
                ReferenceEquals(relationship.Resource, mcp)
                || ReferenceEquals(relationship.Resource, bootstrapSecret),
                $"flutter-ui unexpectedly references '{relationship.Resource.Name}'."));
        Assert.DoesNotContain(builder.Resources, resource => resource.Name.Contains("gateway", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            flutterEnvironment,
            pair => pair.Key.Contains("WatchHomeFeed", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains("DigitalBrainGateway", StringComparison.OrdinalIgnoreCase)
                || pair.Value?.ToString()?.Contains("WatchHomeFeed", StringComparison.OrdinalIgnoreCase) == true
                || pair.Value?.ToString()?.Contains("DigitalBrainGateway", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task ProductionProfile_DoesNotDeclareLocalFlutterOrBootstrapCredential()
    {
        var builder = await CreateAppHostBuilderAsync("--DigitalBrain:Profile=Production");

        Assert.True(builder.ExecutionContext.IsRunMode);
        Assert.Contains(builder.Resources, resource => resource.Name == "mcp");
        Assert.DoesNotContain(builder.Resources, resource => resource.Name == "flutter-ui");
        Assert.DoesNotContain(builder.Resources, resource => resource.Name == "runtime-ui-bootstrap-secret");
    }

    [Fact]
    public async Task PublishMode_RequiresAnExplicitRuntimeProfile()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAppHostBuilderAsync("--publisher", "manifest"));
    }

    [Fact]
    public async Task PublishMode_SkipsEmulatorAndOllamaContainer_UsesConnectionStringPlaceholder()
    {
        // `aspire publish`'s equivalent: no containers should ever be started for this.
        var builder = await CreateAppHostBuilderAsync(
            "--publisher",
            "manifest",
            "--DigitalBrain:Profile=Production");

        Assert.True(builder.ExecutionContext.IsPublishMode);
        Assert.False(builder.ExecutionContext.IsRunMode);

        Assert.DoesNotContain(builder.Resources, r => r.Name == "ollama");

        var storage = Assert.Single(builder.Resources, r => r.Name == "storage");
        Assert.DoesNotContain(storage.Annotations, a => a.GetType().Name == "EmulatorResourceAnnotation");

        var llm = Assert.Single(builder.Resources, r => r.Name == "llm");
        Assert.Equal("ConnectionStringParameterResource", llm.GetType().Name);

        // No local Ollama container in publish mode, so no "embed" model resource either (see Task 15).
        Assert.DoesNotContain(builder.Resources, r => r.Name == "embed");

        Assert.DoesNotContain(builder.Resources, r => r.Name == "flutter-ui");
    }

    private static async Task<Dictionary<string, object>> EvaluateEnvironmentAsync(
        IDistributedApplicationTestingBuilder builder,
        IResource resource)
    {
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return environment;
    }
}
