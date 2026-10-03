using System.Net.Http.Json;
using Azure.Storage.Blobs;
using DigitalBrain.Aspire.Server;
using DigitalBrain.Aspire.Client;
using Microsoft.AspNetCore.DataProtection;
using DigitalBrain.Contracts;
using DigitalBrain.Platform;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Platform.Identity.Directory;
using DigitalBrain.Platform.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

internal static class Worker
{
    internal static async Task Run(string mode)
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.SetMinimumLevel(LogLevel.Error);
        if (mode == "client")
        {
            builder.AddDigitalBrainClient(useAzureClustering: true);
            await using var clientApp = builder.Build();
            clientApp.MapGet("/health", () => Results.Ok());
            clientApp.MapGet("/verify", async (DigitalBrain.IDigitalBrain brain) =>
                await brain.Get<IIdentityDirectory>(IdentityGrains.Directory).AuthenticateAsync("packed-user", "package-test-password") is not null
                    ? Results.Ok() : Results.Unauthorized());
            await clientApp.RunAsync();
            return;
        }
        var maintenance = mode is "inspect" or "checkpoint-interrupt" or "interrupt" or "migrate" or "retry";
        builder.Configuration["DigitalBrain:Auth:Posture"] = "Secured";
        builder.Configuration["DigitalBrain:Identity:Migration:Maintenance"] = maintenance.ToString();
        builder.Configuration["DigitalBrain:MasterKey"] ??= Environment.GetEnvironmentVariable("REHEARSAL_KEY");
        if (mode is "server" or "memory-server")
        {
            builder.AddDigitalBrainServer(server => { if (mode == "server") { server.UseAzureStorage(); } });
        }
        else
        {
            var connection = Environment.GetEnvironmentVariable("REHEARSAL_STORAGE")!;
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddKeyedSingleton(DigitalBrainNames.GrainState, new BlobServiceClient(connection));
            builder.AddDigitalBrainServer(_ => { });
            builder.UseOrleans(silo => silo.UseLocalhostClustering(serviceId: mode == "wrong-service" ? "different-deployment" : "release-rehearsal", clusterId: Guid.NewGuid().ToString("N"))
                .UseInMemoryReminderService()
                .AddAzureBlobGrainStorage("Default", options =>
                {
                    options.BlobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(connection);
                    options.ContainerName = "digitalbrain-v2-state";
                }).ConfigureServices(services =>
                {
                    services.AddKeyedSingleton<IGrainStorageSerializer>("Default", (provider, _) =>
                        new OrleansGrainStorageSerializer(provider.GetRequiredService<Serializer>()));
                    if (mode is "interrupt" or "checkpoint-interrupt")
                    {
                        var original = services.Last(d => d.ServiceType == typeof(IGrainStorage) && Equals(d.ServiceKey, "Default"));
                        services.Remove(original);
                        services.AddKeyedSingleton<IGrainStorage>("Default", (provider, key) =>
                            new InterruptedStorage((IGrainStorage)original.KeyedImplementationFactory!(provider, key), mode == "checkpoint-interrupt"));
                    }
                }));
            builder.Services.Configure<IdentityMigrationOptions>(options =>
            {
                options.SourceSnapshotId = Environment.GetEnvironmentVariable("REHEARSAL_SNAPSHOT");
                options.LegacyGrantBrainIds = ["legacy-brain"];
            });
        }
        if (mode == "memory-server")
        {
            builder.UseOrleans(silo => silo.AddDigitalBrainPlatform());
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        }
        else { builder.AddDigitalBrainPlatform(); }
        await using var app = builder.Build();
        if (!maintenance)
        {
            app.UsePlatformHttp();
            app.MapDigitalBrainPlatform();
        }
        app.MapGet("/health", () => Results.Ok());
        if (mode is "server" or "memory-server") { await app.RunAsync(); return; }
        try { await app.StartAsync(); }
        catch (Exception error) when (
            (mode == "wrong-service" && HasOptionsFailure(error, "ServiceId")) || (mode == "wrong-key" && HasOptionsFailure(error, "MasterKey"))
            || (mode == "blocked" && HasOptionsFailure(error, "DeploymentStorage")))
        { Environment.ExitCode = mode == "wrong-service" ? 75 : mode == "blocked" ? 76 : 74; return; }
        var directory = app.Services.GetRequiredService<IGrainFactory>().GetGrain<IIdentityDirectory>(IdentityGrains.Directory);
        if (maintenance)
        {
            var plan = await directory.InspectMigrationAsync();
            var planFile = Environment.GetEnvironmentVariable("REHEARSAL_PLAN")!;
            if (mode == "inspect") { await File.WriteAllTextAsync(planFile, plan); }
            else { await directory.ApplyMigrationAsync(await File.ReadAllTextAsync(planFile)); }
        }
        else
        {
            if (await directory.AuthenticateAsync("alice", "legacy-password") is null
                || await directory.CanAccessAsync("alice", "other-account", "legacy-brain"))
            { throw new InvalidOperationException("Migrated identity verification failed."); }
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new() }) { BaseAddress = new(app.Urls.Single()) };
            (await client.PostAsJsonAsync("/identity/login", new { PrincipalId = "alice", Password = "legacy-password" })).EnsureSuccessStatusCode();
            var grants = await client.GetFromJsonAsync<Grant[]>("/brains/legacy-brain/grants");
            if (mode == "revoke")
            {
                if (grants?.Length != 1) { throw new InvalidOperationException("Legacy grant missing."); }
                (await client.PostAsJsonAsync("/brains/legacy-brain/grants/revoke", new { AppId = "legacy-app", SemanticTypeId = "person.email", Mode = GrantMode.Always })).EnsureSuccessStatusCode();
            }
            else if (grants?.Length != 0) { throw new InvalidOperationException("Revoked grant returned after restart."); }
            var proof = await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("REHEARSAL_KEY_PROOF")!);
            byte[] unwrapped;
            try { unwrapped = app.Services.GetRequiredService<IKeyWrapper>().Unwrap(proof); }
            catch (System.Security.Cryptography.CryptographicException) when (mode == "wrong-key")
            { await app.StopAsync(); Environment.ExitCode = 74; return; }
            if (!unwrapped.SequenceEqual("persistent-key-proof"u8.ToArray())) { throw new InvalidOperationException("Encryption key changed."); }
        }
        await app.StopAsync();
        Console.WriteLine(mode + " passed.");
    }

    private static bool HasOptionsFailure(Exception error, string name)
        => error is Microsoft.Extensions.Options.OptionsValidationException validation && validation.OptionsName == name
            || error is AggregateException aggregate && aggregate.InnerExceptions.Any(inner => HasOptionsFailure(inner, name))
            || error.InnerException is { } inner && HasOptionsFailure(inner, name);

    private sealed class InterruptedStorage(IGrainStorage inner, bool checkpoint) : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
    {
        public void Participate(ISiloLifecycle lifecycle) => ((ILifecycleParticipant<ISiloLifecycle>)inner).Participate(lifecycle);
        public Task ReadStateAsync<T>(string name, GrainId id, IGrainState<T> state) => inner.ReadStateAsync(name, id, state);
        public Task ClearStateAsync<T>(string name, GrainId id, IGrainState<T> state) => inner.ClearStateAsync(name, id, state);
        public async Task WriteStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            await inner.WriteStateAsync(name, id, state);
            // A real process termination after the durable account write, before the next import.
            if (checkpoint && name == "directory") { Environment.Exit(72); }
            if (!checkpoint && name == "account") { Environment.Exit(73); }
        }
    }
}
