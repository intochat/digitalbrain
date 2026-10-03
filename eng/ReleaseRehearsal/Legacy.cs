// Compiled into the pinned legacy Identity project, never against candidate types.
using DigitalBrain.Identity;
using DigitalBrain.Identity.Directory;
using DigitalBrain.Identity.Grants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

var connection = Environment.GetEnvironmentVariable("REHEARSAL_STORAGE")!;
using var host = new HostBuilder().ConfigureLogging(log => log.SetMinimumLevel(LogLevel.Error))
    .UseOrleans(silo => silo.UseLocalhostClustering(serviceId: "release-rehearsal", clusterId: Guid.NewGuid().ToString("N"))
        .AddAzureBlobGrainStorage("Default", options =>
        {
            options.BlobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(connection);
            options.ContainerName = "digitalbrain-v2-state";
        }).ConfigureServices(services => services.AddKeyedSingleton<IGrainStorageSerializer>("Default",
            (provider, _) => new OrleansGrainStorageSerializer(provider.GetRequiredService<Serializer>())))).Build();
await host.StartAsync();
var storage = host.Services.GetRequiredKeyedService<IGrainStorage>("Default");
var directoryId = GrainId.Create("identity-directory", "intochat-identity-directory");
var grantsId = GrainId.Create("identity-grants", "intochat-grants-legacy-brain");
var directory = new GrainState<IdentityDirectoryState>(new());
var grants = new GrainState<GrantStoreState>(new());
if (args[0] == "seed")
{
    var at = DateTimeOffset.Parse("2026-09-30T12:00:00Z");
    directory.State = new()
    {
        Accounts = [new() { AccountId = "legacy-account", Name = "Legacy", OwnerPrincipalId = "alice", CreatedAt = at }],
        Members = [new() { AccountId = "legacy-account", BrainId = "legacy-brain", PrincipalId = "alice", DisplayName = "Alice", Role = MemberRole.Owner, JoinedAt = at }],
        PasswordHashes = new() { ["alice"] = new Microsoft.AspNetCore.Identity.PasswordHasher<string>().HashPassword("alice", "legacy-password") }
    };
    grants.State = new() { Grants = [new() { AppId = "legacy-app", SemanticTypeId = "person.email", WorkspaceId = "legacy-brain", Mode = GrantMode.Always, GrantedAt = at }] };
    await storage.WriteStateAsync("directory", directoryId, directory);
    await storage.WriteStateAsync("grants", grantsId, grants);
}
else
{
    await storage.ReadStateAsync("directory", directoryId, directory);
    await storage.ReadStateAsync("grants", grantsId, grants);
    if (directory.State!.Accounts.Single().AccountId != "legacy-account" || grants.State!.Grants.Single().AppId != "legacy-app"
        || new Microsoft.AspNetCore.Identity.PasswordHasher<string>().VerifyHashedPassword("alice", directory.State.PasswordHashes["alice"], "legacy-password")
            == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
    { throw new InvalidOperationException("The old version could not read the restored deployment."); }
}
var wrapper = new DigitalBrain.Platform.Secrets.MasterKeyWrapper(Microsoft.Extensions.Options.Options.Create(
    new DigitalBrain.Platform.Secrets.MasterKeyOptions { MasterKey = Environment.GetEnvironmentVariable("REHEARSAL_KEY")! }));
var proofPath = Environment.GetEnvironmentVariable("REHEARSAL_KEY_PROOF")!;
if (args[0] == "seed") { await File.WriteAllTextAsync(proofPath, wrapper.Wrap("persistent-key-proof"u8.ToArray())); }
else if (!wrapper.Unwrap(await File.ReadAllTextAsync(proofPath)).SequenceEqual("persistent-key-proof"u8.ToArray()))
{ throw new InvalidOperationException("Restored key proof failed."); }
await host.StopAsync();
Console.WriteLine("Legacy " + args[0] + " passed.");
