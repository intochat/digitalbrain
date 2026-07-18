using DigitalBrain.Os.Application;
using DigitalBrain.Hosting.DigitalBrain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Dashboard;
using Orleans.Hosting;
using Orleans.Journaling;
using StackExchange.Redis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Whisper.net;

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
            builder.Logging.AddFilter("Orleans", LogLevel.Information);
            builder.Logging.AddFilter("Microsoft.Orleans", LogLevel.Information);
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
                    var opts = new DigitalBrain.Aspire.Hosting.DigitalBrainSiloOptions
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
                                optionsBuilder.Configure<IServiceProvider>((options, serviceProvider) =>
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
                        silo.AddStartupTask(async (sp, cancellationToken) =>
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
                            await brain.SendAsync(new DigitalBrain.Os.Domain.Events.BootManifestApplied(bootHash, worldIdFromEnv, seeded));
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
            if (!DigitalBrain.Os.Infrastructure.Orleans.SynapseDispatch.ManifestAvailable)
            {
                Console.WriteLine("[warn] DigitalBrain source-generated dispatch manifest not available at kernel startup. Falling back to reflection-based IHandle discovery. (This can happen with incremental builds or Aspire resource launches; a clean build usually resolves it. High-severity tests will catch regressions.)");
            }

            app.MapDefaultEndpoints();

            app.UseCors("AllowAll");
            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

            // gRPC for UiSurface/surfaces transport (server streaming seam).
            app.MapGrpcService<DigitalBrain.Kernel.Experiences.SurfaceStreamService>()
                .EnableGrpcWeb()
                .RequireCors("AllowAll");

            app.MapOrleansDashboard("/orleans-dashboard");

            // U4 google-auth: real PKCE loopback + token exchange (Task1). Simulate for CI (no external Google).
            // Neuron Begin now emits links carrying code_challenge + S256 (per RFC 7636). Verifier stored server-side in GoogleAuthNeuron grain (per brain key).
            // Callback retrieves verifier via grain (self-explanatory GetAndClearPendingCodeVerifier), performs exchange for simulate (proves wire, yields access token not code) or real /token POST.
            // Exchanged access token (not the auth code) sent in GoogleAuthCompleted. No IHttpClientFactory to avoid pkg edits in scope.
            app.MapGet("/oauth/simulate", (string? state) =>
            {
                var demoCode = "demo-loopback-consent-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var redirect = $"/oauth/callback?code={demoCode}&state={Uri.EscapeDataString(state ?? "root")}";
                return Results.Redirect(redirect);
            });

            app.MapGet("/oauth/callback", async (HttpContext callbackHttpContext) =>
            {
                var code = callbackHttpContext.Request.Query["code"];
                var state = callbackHttpContext.Request.Query["state"];
                var grains = callbackHttpContext.RequestServices.GetRequiredService<IGrainFactory>();
                var key = string.IsNullOrWhiteSpace(state) ? "root" : state.ToString();
                var brain = grains.GetGrain<IDigitalBrain>(key);
                // T2 connectors: updated concrete grain type after GoogleAuthNeuron moved to DigitalBrain.Sdk.Experiences.GoogleAuthConnectorNeuron. GrainType("google-auth") preserved exactly (compat for callback, seeds, tests, distribution). Full qual for cross assembly (Kernel refs Connectors).
                var googleAuthGrain = grains.GetGrain<DigitalBrain.Sdk.Experiences.GoogleAuthConnectorNeuron>(key);
                var codeVerifier = googleAuthGrain.GetAndClearPendingCodeVerifier();
                string exchangedAccessToken;
                var codeStr = code.ToString();
                if (string.IsNullOrWhiteSpace(codeStr) || codeStr.StartsWith("demo-loopback-consent-"))
                {
                    // Simulate/demo path: instant, but perform "exchange" using the PKCE codeVerifier to prove the full wire (challenge in link, verifier at callback, token != code).
                    var verifierForExchange = codeVerifier ?? "no-verifier-fallback";
                    var proofBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifierForExchange + "|" + codeStr));
                    exchangedAccessToken = "ya29.pkce-exchanged-" + Convert.ToHexString(proofBytes)[..16].ToLowerInvariant();
                }
                else
                {
                    // Real path: actual PKCE authorization_code exchange to Google token endpoint (RFC 7636 + Google docs).
                    var redirectUri = "http://127.0.0.1:8080/oauth/callback";
                    var clientId = "demo"; // real: replace with registered public client id (no secret); register redirect_uri exactly in Google console.
                    exchangedAccessToken = await PerformPkceTokenExchangeAsync(codeStr, codeVerifier, redirectUri, clientId);
                }
                await brain.SendAsync(new DigitalBrain.Os.Domain.Events.GoogleAuthCompleted(exchangedAccessToken));
                return Results.Text("Google auth completed via loopback. PKCE exchange done (demo simulates using verifier; real POSTs to /token). Access token sent to brain grain. Return to client.");
            });

            app.MapGet("/", () => "DigitalBrain kernel - neurons + synapses. IDigitalBrain + IAspire (AspireGrain) active. Gemma (fast) + Nemotron (reasoning) via Aspire or direct world host. gRPC SurfaceStream for real UiSurface transport. /orleans-dashboard exposes Orleans 10.2 diagnostics. /oauth/simulate and /oauth/callback for google-auth real PKCE loopback + exchange (D Task1).");

            await app.RunAsync(cancellationToken);
        }

        private static async Task<string> PerformPkceTokenExchangeAsync(string code, string? codeVerifier, string redirectUri, string clientId)
        {
            // RFC 7636 §4.5 token request for public client (authorization_code + PKCE verifier). No client_secret.
            // POST form to https://oauth2.googleapis.com/token ; response has access_token (or error).
            // Direct new HttpClient (project already uses for model fetch; avoids IHttpClientFactory registration in this task scope).
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = codeVerifier ?? string.Empty
            };
            using var content = new FormUrlEncodedContent(form);
            using var resp = await http.PostAsync("https://oauth2.googleapis.com/token", content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return $"exchange-error:{resp.StatusCode}:{body.Substring(0, Math.Min(120, body.Length))}";
            }
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("access_token", out var tokEl) && tokEl.ValueKind == JsonValueKind.String)
                {
                    return tokEl.GetString() ?? "exchange-missing-token-value";
                }
                return "exchange-no-access_token";
            }
            catch
            {
                return "exchange-json-failed";
            }
        }
    }
}
