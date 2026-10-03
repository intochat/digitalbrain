using System.Net.Http.Json;
using Azure.Storage.Blobs;
using DigitalBrain.Aspire.Server;
using DigitalBrain.Contracts;
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

namespace DigitalBrain.Platform.Tests.E2E;

internal static class MigrationWorker
{
    internal static async Task Run(string mode)
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.SetMinimumLevel(LogLevel.Error);
        var maintenance = mode is "seed" or "rollback" or "inspect" or "checkpoint-interrupt" or "interrupt" or "migrate" or "retry";
        builder.Configuration["DigitalBrain:Auth:Posture"] = "Secured";
        builder.Configuration["DigitalBrain:Identity:Migration:Maintenance"] = maintenance.ToString();
        builder.Configuration["DigitalBrain:MasterKey"] ??= Environment.GetEnvironmentVariable("IDENTITY_TEST_KEY");
        var connection = Environment.GetEnvironmentVariable("IDENTITY_TEST_STORAGE")!;
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddKeyedSingleton(DigitalBrainNames.GrainState, new BlobServiceClient(connection));
        builder.AddDigitalBrainServer(_ => { });
        builder.UseOrleans(silo => silo.UseLocalhostClustering(siloPort: AvailablePort(), gatewayPort: AvailablePort(),
            serviceId: mode == "wrong-service" ? "different-deployment" : "identity-integration", clusterId: Guid.NewGuid().ToString("N"))
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
            options.SourceSnapshotId = Environment.GetEnvironmentVariable("IDENTITY_TEST_SNAPSHOT");
            options.LegacyGrantBrainIds = ["legacy-brain"];
        });
        builder.AddDigitalBrainPlatform();
        await using var app = builder.Build();
        if (!maintenance)
        {
            app.UsePlatformHttp();
            app.MapDigitalBrainPlatform();
        }
        app.MapGet("/health", () => Results.Ok());
        try { await app.StartAsync(); }
        catch (Exception error) when (
            (mode == "wrong-service" && HasOptionsFailure(error, "ServiceId")) || (mode == "wrong-key" && HasOptionsFailure(error, "MasterKey"))
            || (mode == "blocked" && HasOptionsFailure(error, "DeploymentStorage"))
            || (mode == "blocked-after-import" && HasIncompleteMigration(error)))
        { Environment.ExitCode = mode == "wrong-service" ? 75 : mode.StartsWith("blocked", StringComparison.Ordinal) ? 76 : 74; return; }
        if (mode is "wrong-key" or "wrong-service" or "blocked" or "blocked-after-import")
        { throw new InvalidOperationException("Invalid deployment was allowed to start: " + mode); }
        var directory = app.Services.GetRequiredService<IGrainFactory>().GetGrain<IIdentityDirectory>(IdentityGrains.Directory);
        if (mode is "seed" or "rollback")
        {
            var storage = app.Services.GetRequiredKeyedService<IGrainStorage>("Default");
            var serializer = new OrleansGrainStorageSerializer(app.Services.GetRequiredService<Serializer>());
            var directoryState = new GrainState<IdentityDirectoryState>(serializer.Deserialize<IdentityDirectoryState>(
                new BinaryData(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "LegacyState", "directory.orleans")))));
            var grantState = new GrainState<DigitalBrain.Platform.Identity.Grants.GrantStoreState>(serializer.Deserialize<DigitalBrain.Platform.Identity.Grants.GrantStoreState>(
                new BinaryData(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "LegacyState", "grants.orleans")))));
            var directoryId = GrainId.Create("identity-directory", "intochat-identity-directory");
            var grantsId = GrainId.Create("identity-grants", "intochat-grants-legacy-brain");
            if (mode == "seed")
            {
                await storage.WriteStateAsync("directory", directoryId, directoryState);
                await storage.WriteStateAsync("grants", grantsId, grantState);
                await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("IDENTITY_TEST_KEY_PROOF")!,
                    app.Services.GetRequiredService<IKeyWrapper>().Wrap("persistent-key-proof"u8.ToArray()));
            }
            else
            {
                await storage.ReadStateAsync("directory", directoryId, directoryState);
                await storage.ReadStateAsync("grants", grantsId, grantState);
                if (directoryState.State!.MigrationComplete || directoryState.State.Accounts.Single().AccountId != "legacy-account"
                    || grantState.State!.Grants.Single().AppId != "legacy-app")
                { throw new InvalidOperationException("Snapshot rollback did not restore the original records."); }
            }
        }
        else if (maintenance)
        {
            var plan = await directory.InspectMigrationAsync();
            var planFile = Environment.GetEnvironmentVariable("IDENTITY_TEST_PLAN")!;
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
                (await client.PostAsJsonAsync("/brains/legacy-brain/grants/revoke", new { AppId = "legacy-app", SemanticTypeId = "person.email", Mode = GrantMode.ThisChat })).EnsureSuccessStatusCode();
            }
            else if (grants?.Length != 0) { throw new InvalidOperationException("Revoked grant returned after restart."); }
            var proof = await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("IDENTITY_TEST_KEY_PROOF")!);
            var unwrapped = app.Services.GetRequiredService<IKeyWrapper>().Unwrap(proof);
            if (!unwrapped.SequenceEqual("persistent-key-proof"u8.ToArray())) { throw new InvalidOperationException("Encryption key changed."); }
        }
        await app.StopAsync();
        Console.WriteLine(mode + " passed.");
    }

    private static int AvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool HasIncompleteMigration(Exception error)
        => error is InvalidOperationException && error.Message == "Legacy identity state requires an explicit maintenance migration before normal startup."
            || error is AggregateException aggregate && aggregate.InnerExceptions.Any(HasIncompleteMigration)
            || error.InnerException is { } inner && HasIncompleteMigration(inner);

    private static bool HasOptionsFailure(Exception error, string name)
        => error is global::Microsoft.Extensions.Options.OptionsValidationException validation && validation.OptionsName == name
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
