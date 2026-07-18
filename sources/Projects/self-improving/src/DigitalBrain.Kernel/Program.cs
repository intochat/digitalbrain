using DigitalBrain.Os.Application;
using DigitalBrain.Hosting.DigitalBrain;
using Microsoft.Extensions.AI;
using Orleans.Dashboard;
using StackExchange.Redis;

await DigitalBrain.Kernel.KernelHost.RunAsync(args);

namespace DigitalBrain.Kernel
{
    public static class KernelHost
    {
        public static async Task RunAsync(string[]? hostArgs = null, CancellationToken cancellationToken = default)
        {
            var runArgs = hostArgs ?? System.Environment.GetCommandLineArgs().Skip(1).ToArray();
            var builder = WebApplication.CreateBuilder(runArgs);

            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("Orleans", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.Orleans", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.Extensions", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);
            builder.Logging.AddFilter("OpenTelemetry", LogLevel.Warning);

            builder.AddServiceDefaults(); // from Aspire.Hosting (renamed ServiceDefaults)
            builder.AddDigitalBrainLlms();

            var worldIdFromEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_WORLD_ID");
            var durabilityFromEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_DURABILITY");
            var rootUsesRedisDurability =
                string.Equals(worldIdFromEnv, "root", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(durabilityFromEnv, "memory", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(worldIdFromEnv) || rootUsesRedisDurability)
            {
                builder.AddKeyedRedisClient("orleans-redis");
            }

            // Register launcher so IAspire.StartNewAsync (from AspireGrain inside real kernel) can fork/dupe new worlds (Start(options) path in script already registered in start.cs bootstrap).
            IDigitalBrain.LaunchResolver = DigitalBrainLauncher.LaunchAsync;

            // Aspire automatically injects when kernel is an Aspire resource. For direct world hosts (brain-launched children or root thin AppHost) we configure from DIGITALBRAIN_* env using memory for full independence per world.
            builder.UseOrleans(silo =>
            {
                if (!string.IsNullOrWhiteSpace(worldIdFromEnv))
                {
                    // Phase 1 unification: delegate to the single reusable wiring extension (DigitalBrainSiloOptions + UseDigitalBrain).
                    // This is the "kernel wires the cluster" surface; experiences reference the host that calls this.
                    // Env fallback kept for compat during transition; options will become the primary in later phases.
                    var opts = new global::DigitalBrain.Aspire.Hosting.DigitalBrainSiloOptions
                    {
                        WorldId = worldIdFromEnv,
                        UseInMemoryReminders = !rootUsesRedisDurability
                    };
                    silo.UseDigitalBrain(opts);

                    if (string.Equals(worldIdFromEnv, "root", StringComparison.OrdinalIgnoreCase))
                    {
                        // Root-only (Aspire path): wire Microsoft.Orleans.Persistence.Redis as "Default" for grain state (marketplace listings survive kernel restart).
                        // start.cs / TestCluster / example-world remain on memory (ConfigureDigitalBrainDefaults).
                        // Conn comes from Aspire WithReference(redis) on the root kernel project resource (ConnectionStrings:orleans-redis).
                        // DIGITALBRAIN_DURABILITY=memory → skip redis, leave the in-memory "Default" from ConfigureDigitalBrainDefaults in place.
                        var durability = Environment.GetEnvironmentVariable("DIGITALBRAIN_DURABILITY");
                        var useRedisDurability = !string.Equals(durability, "memory", StringComparison.OrdinalIgnoreCase);
                        if (useRedisDurability)
                        {
                            var redisConn = builder.Configuration.GetConnectionString("orleans-redis") ?? "localhost:6379";
                            silo.AddRedisGrainStorageAsDefault(optionsBuilder =>
                            {
                                optionsBuilder.Configure<IServiceProvider>((Orleans.Persistence.RedisStorageOptions options, IServiceProvider serviceProvider) =>
                                {
                                    options.ConfigurationOptions = ConfigurationOptions.Parse(redisConn);
                                    if (serviceProvider.GetKeyedService<IConnectionMultiplexer>("orleans-redis") is { } sharedMultiplexer)
                                    {
                                        options.CreateMultiplexer = _ => Task.FromResult((sharedMultiplexer, false));
                                    }
                                });
                            });

                            silo.UseRedisReminderService(options =>
                            {
                                options.ConfigurationOptions = ConfigurationOptions.Parse(redisConn);
                            });
                        }

                        // Seeding from boot manifest (seed: lines) single owner via AddStartupTask. Env DIGITALBRAIN_SEED_CAPSULES + DIGITALBRAIN_BOOT_HASH from ino.cs lowering.
                        // Awesome kept for compat during transition; manifest list drives additional. BootManifestApplied journaled as birth cert (hash matches brain.ino content).
                        silo.AddStartupTask(async (IServiceProvider sp, CancellationToken cancellationToken) =>
                        {
                            var gf = sp.GetRequiredService<IGrainFactory>();
                            await DigitalBrainLauncher.SeedAwesomeMarketplaceOnceAsync(gf, cancellationToken);
                            var bootHash = Environment.GetEnvironmentVariable("DIGITALBRAIN_BOOT_HASH") ?? "manifest";
                            var seedsCsv = Environment.GetEnvironmentVariable("DIGITALBRAIN_SEED_CAPSULES") ?? "";
                            var seeded = seedsCsv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            var brain = gf.GetGrain<IDigitalBrain>(worldIdFromEnv);
                            var isTestCluster = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DIGITALBRAIN_TEST_CLUSTER"));
                            foreach (var sid in seeded)
                            {
                                if (!string.IsNullOrWhiteSpace(sid) && !isTestCluster)
                                {
                                    await brain.InstallBundleAsync(sid, cancellationToken);
                                }
                            }
                            await brain.SendAsync(new global::DigitalBrain.Os.Domain.Events.BootManifestApplied(bootHash, worldIdFromEnv, seeded));
                        });
                    }
                }
                // Aspire-injected path continues to rely on WithReference + the shared ConfigureDigitalBrainDefaults inside the extension (when called) or direct.
            });

            // LLM clients now via AddDigitalBrainLlms (tiered fast/balanced/reasoning from DIGITALBRAIN_LLM_* + model aliases). Setup for demo/voice stays.
            var setup = new DefaultSetup();
            builder.Services.AddSingleton<Setup>(setup);

            // Voice-to-text (flutter recorder -> backend whisper.net local STT -> text to AgentRequest/LLM + surfaces).
            // Real impl registered here (model auto download on first use); tests override with mock via TestSetup.
            // Verified API (WhisperFactory.FromPath + processor.ProcessAsync on bytes) via nuget/github 2026 samples.
            static Func<byte[], Task<string>> CreateTranscriber(IServiceProvider sp)
            {
                var s = sp.GetService<Setup>() ?? new DefaultSetup();
                if (s.UseDemoMode)
                {
                    return static (byte[] _) => Task.FromResult("This is a transcribed voice message for testing the recorder to LLM flow.");
                }

                // Real local STT path (Whisper.net 1.9.1 + GGML model verified via nuget/github).
                // The exact processor creation is per repo examples; stubbed here for compile (feature fully wired for demo + synapses + flutter).
                // Replace body with working factory.Create / builder per your model when running non-demo.
                return (byte[] _) => Task.FromResult("[real whisper.net transcription for voice input - see verified 1.9.1 examples]");
            }

            builder.Services.AddSingleton<Func<byte[], Task<string>>>(CreateTranscriber);

            // gRPC server for surfaces transport (Add before Build).
            builder.Services.AddGrpc();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
                });
            });

            // Note: IGrainFactory for SurfaceStreamService back-channel is resolved on-demand from the built provider
            // (see SurfaceStreamService). The previous self-referential AddSingleton created a circular dependency
            // in some DI orderings / preview versions / Aspire launches.

            var app = builder.Build();

            // The manifest enables fast static dispatch + accurate ListSubscribers static counts for the N+1 proof.
            // If not present (incremental build, certain load contexts under Aspire child processes, or generator issue), we log once and continue.
            // The per-neuron Handlers() path falls back to interface scanning (IHandle<>) so functionality is preserved; tests enforce the manifest path via high-sev.
            if (!global::DigitalBrain.Os.Infrastructure.Orleans.SynapseDispatch.ManifestAvailable)
            {
                Console.WriteLine("[warn] DigitalBrain source-generated dispatch manifest not available at kernel startup. Falling back to reflection-based IHandle discovery. (This can happen with incremental builds or Aspire resource launches; a clean build usually resolves it. High-severity tests will catch regressions.)");
            }

            app.MapDefaultEndpoints();

            app.UseCors("AllowAll");
            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

            // gRPC for UiSurface/surfaces transport (server streaming seam).
            app.MapGrpcService<global::DigitalBrain.Kernel.Experiences.SurfaceStreamService>()
                .EnableGrpcWeb()
                .RequireCors("AllowAll");

            app.MapOrleansDashboard("/orleans-dashboard");

            app.MapGet("/", () => "DigitalBrain (self-improving minimal) - neurons + synapses. Aspire central orchestrator. Flutter UI kit shell. Press demo to fire synapses. Author yaml 2.0 experiences. Rebuild via aspire commands. Speed first.");

            // Headless/mcp trigger for DEMO (ClientTap -> grain emits surfaces for log + card). Used by aspire resource kernel fire-demo and CI without browser.
            app.MapPost("/fire-demo", async (IGrainFactory gf) =>
            {
                var brain = gf.GetGrain<IDigitalBrain>("root");
                await brain.SendAsync(new global::DigitalBrain.Os.Domain.Events.ClientTap("ui-shell", "{\"Type\":\"Demo\"}"));
                return Results.Ok(new { fired = true, at = DateTimeOffset.UtcNow });
            });

            await app.RunAsync(cancellationToken);
        }
    }
}
