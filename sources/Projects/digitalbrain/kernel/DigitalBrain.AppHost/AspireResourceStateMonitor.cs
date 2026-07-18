using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;

using System.Collections.Concurrent;
using Aspire.Hosting;

namespace DigitalBrain.Hosting;

public sealed class AspireResourceStateMonitor(
    ResourceNotificationService notificationService,
    ILogger<AspireResourceStateMonitor> logger) : BackgroundService
{
    public static string? RedisConnectionString;
    public static readonly ConcurrentDictionary<string, CustomResourceSnapshot> LatestSnapshots = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AspireResourceStateMonitor: Starting live resource status monitoring...");

        await foreach (var resourceEvent in notificationService.WatchAsync(stoppingToken))
        {
            var resource = resourceEvent.Resource;
            var snapshot = resourceEvent.Snapshot;

            if (snapshot == null) continue;

            LatestSnapshots[resource.Name] = snapshot;

            logger.LogInformation("AspireResourceStateMonitor: Resource '{Name}' State: {State}", resource.Name, snapshot.State?.Text);
            foreach (var prop in snapshot.Properties)
            {
                logger.LogInformation("Resource '{Name}' Property: {Key}={Value}", resource.Name, prop.Name, prop.Value);
            }
            foreach (var url in snapshot.Urls)
            {
                logger.LogInformation("Resource '{Name}' Url: {Url}", resource.Name, url.Url);
            }

            if (string.Equals(resource.Name, "orleans-redis", StringComparison.OrdinalIgnoreCase))
            {
                var connStrProp = snapshot.Properties.FirstOrDefault(p => string.Equals(p.Name, "resource.connectionString", StringComparison.OrdinalIgnoreCase));
                var connStrVal = connStrProp?.Value?.ToString();
                if (!string.IsNullOrEmpty(connStrVal))
                {
                    RedisConnectionString = connStrVal;
                    logger.LogInformation("AspireResourceStateMonitor: Captured Redis connection string: {Conn}", RedisConnectionString);
                }
            }

            if (string.Equals(resource.Name, "kernel", StringComparison.OrdinalIgnoreCase))
            {
                var httpUrl = snapshot.Urls.FirstOrDefault(u => u.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))?.Url;
                if (!string.IsNullOrEmpty(httpUrl))
                {
                    try
                    {
                        var u = new Uri(httpUrl);
                        var port = u.Port;
                        File.WriteAllText("kernel_port.txt", port.ToString());
                        logger.LogInformation("AspireResourceStateMonitor: Wrote dynamic kernel port {Port} to kernel_port.txt", port);

                        // Also write to Flutter Web folder so the browser can fetch it dynamically
                        var pathsToTry = new[]
                        {
                            Path.Combine(AppContext.BaseDirectory, "../../../UI/flutter/web"),
                            Path.Combine(AppContext.BaseDirectory, "../../UI/flutter/web"),
                            Path.Combine(Directory.GetCurrentDirectory(), "../../UI/flutter/web"),
                            @"E:\digitalbrain\UI\flutter\web"
                        };

                        foreach (var dir in pathsToTry)
                        {
                            if (Directory.Exists(dir))
                            {
                                var targetFile = Path.Combine(dir, "kernel_port.txt");
                                File.WriteAllText(targetFile, port.ToString());
                                logger.LogInformation("AspireResourceStateMonitor: Successfully wrote port to {Path}", targetFile);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "AspireResourceStateMonitor: Failed to write kernel_port.txt");
                    }
                }
            }

            // Detect failed state or non-zero exit code
            bool isFailed = string.Equals(snapshot.State?.Text, "FailedToStart", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(snapshot.State?.Text, "Failed", StringComparison.OrdinalIgnoreCase) ||
                            (snapshot.ExitCode.HasValue && snapshot.ExitCode.Value != 0);

            if (isFailed)
            {
                logger.LogWarning("AspireResourceStateMonitor: Detected failed resource '{Name}' (State: {State}, ExitCode: {ExitCode})",
                    resource.Name, snapshot.State?.Text, snapshot.ExitCode);

                // Fetch logs if possible (simplified/mocked summary for the demo/run)
                string logs = $"Simulated process exit or port conflict logs for resource {resource.Name}.";
                string errorSummary = $"Resource {resource.Name} exited with code {snapshot.ExitCode ?? -1} and state {snapshot.State?.Text}";

                _ = EmitResourceFailedAsync(resource.Name, snapshot.ExitCode, errorSummary, logs, stoppingToken);
            }
        }
    }

    private async Task EmitResourceFailedAsync(
        string resourceName,
        int? exitCode,
        string errorSummary,
        string logs,
        CancellationToken stoppingToken)
    {
        try
        {
            // Force-load the grain implementation assemblies to ensure Orleans client assembly scanning discovers grain implementations
            try
            {
                System.Reflection.Assembly.Load("DigitalBrain.Kernel");
                System.Reflection.Assembly.Load("DigitalBrain.SDK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AspireResourceStateMonitor: Failed to eagerly load grain implementation assemblies.");
            }

            logger.LogInformation("AspireResourceStateMonitor: Queueing self-healing trigger for '{Name}'...", resourceName);

            // Wait for Redis connection string to become available
            int waitAttempts = 30;
            while (string.IsNullOrEmpty(RedisConnectionString))
            {
                if (stoppingToken.IsCancellationRequested) return;
                logger.LogWarning("AspireResourceStateMonitor: Redis connection string not available yet for '{Name}'. Waiting...", resourceName);
                await Task.Delay(1000, stoppingToken);
                waitAttempts--;
                if (waitAttempts <= 0)
                {
                    logger.LogError("AspireResourceStateMonitor: Timed out waiting for Redis connection string for '{Name}'", resourceName);
                    return;
                }
            }

            logger.LogInformation("AspireResourceStateMonitor: Connecting lazy Orleans client to broadcast synapse for '{Name}'...", resourceName);

            IHost? clientHost = null;
            int maxRetries = 10;
            int delayMs = 3000;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    clientHost = new HostBuilder()
                        .UseOrleansClient(cb =>
                        {
                            cb.Configure<Orleans.Configuration.ClusterOptions>(options =>
                            {
                                options.ClusterId = "digitalbrain-cluster";
                                options.ServiceId = "digitalbrain-cluster";
                            });
                            cb.UseRedisClustering(options =>
                            {
                                var config = StackExchange.Redis.ConfigurationOptions.Parse(RedisConnectionString);
                                config.AbortOnConnectFail = false;
                                options.ConfigurationOptions = config;
                            });
                            cb.AddMemoryStreams("synapse");
                        })
                        .Build();

                    await clientHost.StartAsync(stoppingToken);
                    logger.LogInformation("AspireResourceStateMonitor: Orleans client connected successfully on attempt {Attempt} for '{Name}'", attempt, resourceName);
                    break;
                }
                catch (Exception ex)
                {
                    clientHost?.Dispose();
                    clientHost = null;
                    if (attempt == maxRetries)
                    {
                        throw;
                    }
                    logger.LogWarning(ex, "AspireResourceStateMonitor: Connection attempt {Attempt} of {MaxRetries} failed for '{Name}'. Retrying in {DelayMs}ms...",
                        attempt, maxRetries, resourceName, delayMs);
                    await Task.Delay(delayMs, stoppingToken);
                }
            }

            var client = clientHost!.Services.GetRequiredService<IClusterClient>();
            var header = SynapseFactory.CreateHeader(
                callerId: new NeuronId("sys.aspire"),
                callerType: "IAspireRuntimeNeuron",
                receiverId: new NeuronId("digitalbrain.sdk.aspire.runtime.specs.selfhealing"),
                receiverType: "SelfHealing"
            );

            var synapse = new ResourceFailed(resourceName, exitCode ?? -1, errorSummary, logs) { Headers = header };

            // Route synapse via the GatewayNeuron grain to bridge the process boundary
            var gateway = client.GetGrain<IGatewayNeuron>(Guid.Empty);
            await gateway.RouteAsync(synapse);

            logger.LogInformation("AspireResourceStateMonitor: Successfully emitted ResourceFailed synapse for '{Name}'", resourceName);

            await clientHost.StopAsync(stoppingToken);
            clientHost.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AspireResourceStateMonitor: Failed to emit ResourceFailed synapse for '{Name}'", resourceName);
        }
    }
}
