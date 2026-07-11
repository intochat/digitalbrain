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

        var storage = Assert.Single(builder.Resources, r => r.Name == "storage");
        Assert.Contains(storage.Annotations, a => a.GetType().Name == "EmulatorResourceAnnotation");

        Assert.Contains(builder.Resources, r => r.Name == "ollama" && r.GetType().Name == "OllamaResource");

        var llm = Assert.Single(builder.Resources, r => r.Name == "llm");
        Assert.Equal("OllamaModelResource", llm.GetType().Name);

        var embed = Assert.Single(builder.Resources, r => r.Name == "embed");
        Assert.Equal("OllamaModelResource", embed.GetType().Name);

        // Local Whisper (speaches) container for voice-to-text, always present in run mode (see Task 16).
        Assert.Contains(builder.Resources, r => r.Name == "whisper" && r.GetType().Name == "ContainerResource");

        // Sync blob container (checkpoint backup/restore, M11 Task 20) — unconditional like grainstate/journal,
        // not gated by isRunMode, but still present here as a regression guard for the resource wiring itself.
        Assert.Contains(builder.Resources, r => r.Name == "sync" && r.GetType().Name == "AzureBlobStorageResource");

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
            Assert.Single(builder.Resources, r => r.Name == "v2-ui-bootstrap-secret"));
        Assert.True(bootstrapSecret.Secret);
        var feedIntegrityKey = Assert.IsType<ParameterResource>(
            Assert.Single(builder.Resources, r => r.Name == "v2-ui-feed-integrity-key"));
        Assert.True(feedIntegrityKey.Secret);
        var journalIntegrityKey = Assert.IsType<ParameterResource>(
            Assert.Single(builder.Resources, r => r.Name == "v2-journal-integrity-key"));
        Assert.True(journalIntegrityKey.Secret);

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
        Assert.Equal(salesforceCallback, flutterEnvironment["DIGITALBRAIN_SALESFORCE_OAUTH_CALLBACK"]);

        var mcpEnvironment = await EvaluateEnvironmentAsync(builder, mcp);
        Assert.Same(bootstrapSecret, mcpEnvironment["DigitalBrain__V2__Ui__BootstrapSecret"]);
        Assert.Same(feedIntegrityKey, mcpEnvironment["DigitalBrain__V2__Ui__FeedIntegrityKey"]);
        Assert.Same(journalIntegrityKey, mcpEnvironment["DigitalBrain__V2__JournalIntegrityKey"]);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var operationPath = Assert.IsType<string>(mcpEnvironment["DigitalBrain__V2__OperationStorePath"]);
        var projectionPath = Assert.IsType<string>(mcpEnvironment["DigitalBrain__V2__ProjectionStorePath"]);
        var sessionPath = Assert.IsType<string>(mcpEnvironment["DigitalBrain__V2__SessionStorePath"]);
        var feedPath = Assert.IsType<string>(mcpEnvironment["DigitalBrain__V2__Ui__FeedStorePath"]);
        Assert.All([operationPath, projectionPath, sessionPath, feedPath], path =>
        {
            Assert.StartsWith(localAppData, path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Path.Combine("hosts", "DigitalBrain.AppHost", ".digitalbrain"), path, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal("operations.jsonl", Path.GetFileName(operationPath));
        Assert.Equal("projections.jsonl", Path.GetFileName(projectionPath));
        Assert.Equal("sessions.jsonl", Path.GetFileName(sessionPath));
        Assert.Equal("ui-feed.jsonl", Path.GetFileName(feedPath));
        Assert.Single(new[] { operationPath, projectionPath, sessionPath, feedPath }
            .Select(Path.GetDirectoryName)
            .Distinct(StringComparer.OrdinalIgnoreCase));

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
        Assert.DoesNotContain(builder.Resources, resource => resource.Name == "v2-ui-bootstrap-secret");
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

        // No local Whisper container in publish mode either (see Task 16).
        Assert.DoesNotContain(builder.Resources, r => r.Name == "whisper");

        // Sync blob container still present in publish mode (AddAzureStorage produces a valid real-Azure
        // resource on its own — same reasoning as grainstate/journal, see Task 20).
        Assert.Contains(builder.Resources, r => r.Name == "sync" && r.GetType().Name == "AzureBlobStorageResource");
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
