#pragma warning disable ASPIRECERTIFICATES001

using Aspire.Hosting.Orleans;
using Aspire.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Hosting.DigitalBrain;

public sealed class DigitalBrainResource
{
    internal DigitalBrainResource(string name, IDistributedApplicationBuilder appBuilder)
    {
        Name = name;
        AppBuilder = appBuilder;
    }

    internal string Name { get; }
    internal IDistributedApplicationBuilder AppBuilder { get; }
    internal IResourceBuilder<RedisResource>? Redis { get; private set; }
    internal OrleansService? Orleans { get; private set; }
    public IResourceBuilder<ProjectResource>? Kernel { get; private set; }
    internal List<IResourceBuilder<ProjectResource>> Silos { get; } = new();
    internal Dictionary<string, IResourceBuilder<ParameterResource>> Secrets { get; } = new();
    internal DeclaredEmbeddingModel? EmbeddingModel { get; set; }
    internal DeclaredVoiceModel? VoiceModel { get; set; }

    internal void InitializeInfrastructure()
    {
        Redis = AppBuilder.AddRedis("orleans-redis")
            .WithoutHttpsCertificate();
        Orleans = AppBuilder.AddOrleans($"{Name}-cluster")
            .WithClusterId("digitalbrain-cluster")
            .WithServiceId("digitalbrain-cluster")
            .WithClustering(Redis)
            .WithMemoryGrainStorage("digitalbrain")
            .WithMemoryReminders();

        var kernel = AppBuilder.AddProject<Projects.DigitalBrain_Kernel>("kernel")
            .WithReference(Orleans!)
            .WaitFor(Redis!)
            .WithHttpsEndpoint(name: "kernel-https")
            .WithHttpEndpoint(name: "kernel-http")
            .WithCommand(
                "heal-topography",
                "Heal Topography",
                async context =>
                {
                    try
                    {
                        var logger = context.Services.GetRequiredService<ILogger<DigitalBrainResource>>();
                        logger.LogInformation("Dashboard custom command: Heal Topography triggered!");

                        // Get failed resources
                        var failedResources = new List<FailedResourceDetails>();
                        foreach (var kvp in AspireResourceStateMonitor.LatestSnapshots)
                        {
                            var name = kvp.Key;
                            var snapshot = kvp.Value;
                            
                            // Check if failed or not running
                            bool isFailed = string.Equals(snapshot.State?.Text, "FailedToStart", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(snapshot.State?.Text, "Failed", StringComparison.OrdinalIgnoreCase) ||
                                            (snapshot.ExitCode.HasValue && snapshot.ExitCode.Value != 0) ||
                                            string.Equals(snapshot.State?.Text, "Exited", StringComparison.OrdinalIgnoreCase);

                            if (isFailed)
                            {
                                logger.LogWarning("Found failed resource: {Name}", name);
                                failedResources.Add(new FailedResourceDetails(
                                    name,
                                    snapshot.State?.Text ?? "Unknown",
                                    snapshot.ExitCode,
                                    $"Resource {name} exited with code {snapshot.ExitCode ?? -1}",
                                    $"Simulated logs for failed resource {name}"
                                ));
                            }
                        }

                        // For demonstration/manual testing: if there are no failed resources, we can simulate one (e.g. flutter-web)
                        // so that clicking the button always runs the beautiful developer self-healing loop!
                        if (failedResources.Count == 0)
                        {
                            logger.LogInformation("No failed resources found. Injecting a mock failed resource 'flutter-web' to demonstrate self-healing.");
                            failedResources.Add(new FailedResourceDetails(
                                "flutter-web",
                                "FailedToStart",
                                1,
                                "Port 5800 already in use",
                                "Error: listen EADDRINUSE: address already in use :::5800"
                            ));
                        }

                        var redisConn = AspireResourceStateMonitor.RedisConnectionString;
                        if (string.IsNullOrEmpty(redisConn))
                        {
                            return CommandResults.Failure("Redis connection string not captured yet. Please wait a moment.");
                        }

                        // Spin up Orleans client and send the HealTopographyRequest synapse!
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var clientHost = new HostBuilder()
                                    .UseOrleansClient(cb =>
                                    {
                                        cb.Configure<Orleans.Configuration.ClusterOptions>(options =>
                                        {
                                            options.ClusterId = "digitalbrain-cluster";
                                            options.ServiceId = "digitalbrain-cluster";
                                        });
                                        cb.UseRedisClustering(options =>
                                        {
                                            var config = StackExchange.Redis.ConfigurationOptions.Parse(redisConn);
                                            config.AbortOnConnectFail = false;
                                            options.ConfigurationOptions = config;
                                        });
                                        cb.AddMemoryStreams("synapse");
                                    })
                                    .Build();

                                await clientHost.StartAsync();
                                var client = clientHost.Services.GetRequiredService<IClusterClient>();
                                
                                var header = SynapseFactory.CreateHeader(
                                    callerId: new NeuronId("sys.aspire"),
                                    callerType: "IAspireRuntimeNeuron",
                                    receiverId: new NeuronId("digitalbrain.sdk.aspire.runtime.specs.topographyhealer"),
                                    receiverType: "TopographyHealer"
                                );

                                var synapse = new HealTopographyRequest(failedResources) { Headers = header };
                                var gateway = client.GetGrain<IGatewayNeuron>(Guid.Empty);
                                await gateway.RouteAsync(synapse);

                                logger.LogInformation("Successfully sent HealTopographyRequest to Orleans!");
                                
                                await clientHost.StopAsync();
                                clientHost.Dispose();
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to send HealTopographyRequest to Orleans.");
                            }
                        });

                        return CommandResults.Success("Self-healing loop triggered for failed resources!");
                    }
                    catch (Exception ex)
                    {
                        return CommandResults.Failure($"Error triggering self-healing: {ex.Message}");
                    }
                }
            );

        Kernel = kernel;
        Silos.Add(kernel);
    }

    AiDomainBuilder? _aiConfig;

    public DigitalBrainResource WithDomain<TProject>(
        Action<AiDomainBuilder>? configure = null)
        where TProject : IProjectMetadata, new()
    {
        EnsureRedis();

        var siloName = typeof(TProject).Name
            .Replace("DigitalBrain_Domains_", "", StringComparison.Ordinal)
            .Replace("_", "-", StringComparison.Ordinal)
            .ToLowerInvariant();

        var silo = AppBuilder.AddProject<TProject>(siloName)
            .WithReference(Orleans!)
            .WaitFor(Redis!)
            .WithHttpEndpoint();

        if (configure is not null)
        {
            var ai = new AiDomainBuilder(this);
            configure(ai);
            ai.ApplyTo(silo);
            _aiConfig = ai;

            foreach (var other in Silos)
                if (!ReferenceEquals(other, silo))
                    ai.ApplyTo(other);
        }
        else if (_aiConfig is { } existing)
        {
            existing.ApplyTo(silo);
        }

        Silos.Add(silo);
        return this;
    }

    internal IResourceBuilder<ParameterResource> SecretParam(string parameterName, string description)
    {
        if (Secrets.TryGetValue(parameterName, out var existing)) return existing;

        // Retrieve secrets from environment as fallbacks, but allow Aspire to prompt if completely missing
        if (AppBuilder.Configuration[$"Parameters:{parameterName}"] is null)
        {
            string? fallback = null;
            if (parameterName == "grok-api-key")
            {
                fallback = Environment.GetEnvironmentVariable("XAI_API_KEY")
                    ?? Environment.GetEnvironmentVariable("DigitalBrain__Ai__GrokApiKey")
                    ?? Environment.GetEnvironmentVariable("grok-api-key");
            }
            else if (parameterName == "openai-api-key")
            {
                fallback = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? Environment.GetEnvironmentVariable("DigitalBrain__Ai__OpenAiApiKey")
                    ?? Environment.GetEnvironmentVariable("openai-api-key");
            }
            else if (parameterName == "anthropic-api-key")
            {
                fallback = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                    ?? Environment.GetEnvironmentVariable("DigitalBrain__Ai__AnthropicApiKey")
                    ?? Environment.GetEnvironmentVariable("anthropic-api-key");
            }

            if (!string.IsNullOrEmpty(fallback))
            {
                AppBuilder.Configuration[$"Parameters:{parameterName}"] = fallback;
            }
        }

        var parameter = AppBuilder.AddParameter(parameterName, secret: true)
            .WithDescription(description, enableMarkdown: true);
        Secrets[parameterName] = parameter;
        return parameter;
    }

    void EnsureRedis()
    {
        if (Redis is null || Orleans is null)
            throw new InvalidOperationException(
                "Infrastructure not initialized — call AddDigitalBrain() before WithDomain<>().");
    }
}
