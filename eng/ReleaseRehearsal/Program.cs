using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Testing;
using DigitalBrain.Testing.E2E;

if (args.Length > 0) { await Worker.Run(args[0]); return; }
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
var ct = deadline.Token;
var directory = Environment.GetEnvironmentVariable("REHEARSAL_OUTPUT")!;
var candidate = Assembly.GetExecutingAssembly().Location;
var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var environment = new Dictionary<string, string>
{
    ["REHEARSAL_KEY"] = key,
    ["REHEARSAL_PLAN"] = Path.Combine(directory, "migration-plan.txt"),
    ["REHEARSAL_KEY_PROOF"] = Path.Combine(directory, "key-proof.txt")
};

await using var compositionVolume = await DurableStorageVolume.CreateAsync(cancellationToken: ct);
await File.WriteAllTextAsync(Path.Combine(directory, "composition-volume.txt"), compositionVolume.Name, ct);
// Package-based AppHost -> server -> client, including explicit selection among two brains.
await using (var session = await AspireTestSession.StartAsync(
    new TestExecutionOptions { ServerResourceName = "second-server", StartupTimeout = TimeSpan.FromMinutes(3) }, builder =>
    {
        foreach (var name in new[] { "first", "second" })
        {
            builder.Configuration[$"Parameters:{name}-digitalbrain-master-key"] = key;
            var brain = builder.AddDigitalBrain(name, persistentStorage: false, dataVolume: name == "second" ? compositionVolume.Name : null, serviceId: name == "second" ? "stable-composition" : name, options: new() { UseAzureStorage = true, ClusterId = Guid.NewGuid().ToString("N") });
            var server = builder.AddExecutable(name + "-server", "dotnet", AppContext.BaseDirectory, candidate, "server")
                .WithReference(brain).WithHttpEndpoint(name: "http", env: "ASPNETCORE_HTTP_PORTS")
                .WithHttpHealthCheck("/health");
            if (name == "second")
            {
                builder.AddExecutable("packed-client", "dotnet", AppContext.BaseDirectory, candidate, "client")
                    .WithReference(brain.AsClient()).WaitFor(server)
                    .WithHttpEndpoint(name: "http", env: "ASPNETCORE_HTTP_PORTS").WithHttpHealthCheck("/health");
            }
        }
    }, ct))
{
    var identity = session.Brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
    await identity.RegisterAsync("packed-user", "package-test-password", "Package test", ct);
    (await session.HttpClient.PostAsJsonAsync("/identity/login", new { PrincipalId = "packed-user", Password = "package-test-password" }, ct)).EnsureSuccessStatusCode();
    await session.App.ResourceNotifications.WaitForResourceHealthyAsync("packed-client", ct);
    using var consumingProcess = session.App.CreateHttpClient("packed-client", "http");
    (await consumingProcess.GetAsync("/verify", ct)).EnsureSuccessStatusCode();
    using var other = session.App.CreateHttpClient("first-server", "http");
    using var denied = await other.PostAsJsonAsync("/identity/login", new { PrincipalId = "packed-user", Password = "package-test-password" }, ct);
    if (denied.StatusCode != HttpStatusCode.Unauthorized) { throw new InvalidOperationException("Two-brain isolation failed."); }
}
await compositionVolume.WaitUntilReleasedAsync(ct);
await using (var renamed = await AspireTestSession.StartAsync(new TestExecutionOptions(), builder =>
{
    builder.Configuration["Parameters:renamed-digitalbrain-master-key"] = key;
    var brain = builder.AddDigitalBrain("renamed", persistentStorage: false, dataVolume: compositionVolume.Name,
        serviceId: "stable-composition", options: new() { UseAzureStorage = true, ClusterId = Guid.NewGuid().ToString("N") });
    builder.AddExecutable("renamed-server", "dotnet", AppContext.BaseDirectory, candidate, "server")
        .WithReference(brain).WithHttpEndpoint(name: "http", env: "ASPNETCORE_HTTP_PORTS").WithHttpHealthCheck("/health");
}, ct))
{
    (await renamed.HttpClient.PostAsJsonAsync("/identity/login", new { PrincipalId = "packed-user", Password = "package-test-password" }, ct)).EnsureSuccessStatusCode();
}
compositionVolume.Complete();
await using (var memory = await AspireTestSession.StartAsync(new TestExecutionOptions(), builder =>
{
    builder.Configuration["Parameters:memory-digitalbrain-master-key"] = key;
    var brain = builder.AddDigitalBrain("memory");
    builder.AddExecutable("memory-server", "dotnet", AppContext.BaseDirectory, candidate, "memory-server")
        .WithReference(brain).WithHttpEndpoint(name: "http", env: "ASPNETCORE_HTTP_PORTS").WithHttpHealthCheck("/health");
}, ct))
{
    await memory.Brain.Get<IIdentityDirectory>(IdentityGrains.Directory).RegisterAsync("memory-user", "package-test-password", "Memory test", ct);
    (await memory.HttpClient.PostAsJsonAsync("/identity/login", new { PrincipalId = "memory-user", Password = "package-test-password" }, ct)).EnsureSuccessStatusCode();
}
Console.WriteLine("Packed AppHost/server/client and two-brain isolation passed.");

await using var volume = await DurableStorageVolume.CreateAsync(cancellationToken: ct);
await File.WriteAllTextAsync(Path.Combine(directory, "volume.txt"), volume.Name, ct);
await using (var builder = DistributedApplicationTestingBuilder.Create([]))
{
    var storage = builder.AddAzureStorage("upgrade-storage").RunAsEmulator(emulator => emulator.WithDataVolume(volume.Name).WithArgs("--silent"));
    var blobs = storage.AddBlobs("upgrade-blobs");
    await using var app = await builder.BuildAsync(ct);
    await app.StartAsync(ct);
    await app.ResourceNotifications.WaitForResourceHealthyAsync(storage.Resource.Name, ct);
    environment["REHEARSAL_STORAGE"] = await app.GetConnectionStringAsync(blobs.Resource.Name, ct) ?? throw new InvalidOperationException("Missing storage connection.");
    var legacy = Environment.GetEnvironmentVariable("REHEARSAL_LEGACY")!;
    await Run(legacy, "seed", 0);
    var blobService = new BlobServiceClient(environment["REHEARSAL_STORAGE"]);
    var backup = await Snapshot(blobService);
    if (backup["digitalbrain-v2-state"].Count != 2) { throw new InvalidOperationException("Legacy writer did not create exactly the two expected identity records."); }
    var snapshot = JsonSerializer.SerializeToUtf8Bytes(backup);
    await File.WriteAllBytesAsync(Path.Combine(directory, "legacy-snapshot.json"), snapshot, ct);
    environment["REHEARSAL_SNAPSHOT"] = Convert.ToHexStringLower(SHA256.HashData(snapshot));
    await Run(candidate, "inspect", 0);
    if (!JsonSerializer.SerializeToUtf8Bytes(await Snapshot(blobService)).SequenceEqual(snapshot))
    { throw new InvalidOperationException("Read-only migration inspection changed blob storage."); }
    await Run(candidate, "checkpoint-interrupt", 72);
    await Run(candidate, "blocked", 76);
    await Run(candidate, "interrupt", 73);
    await Run(candidate, "migrate", 0);
    await Run(candidate, "revoke", 0);
    await Run(candidate, "retry", 0);
    await Run(candidate, "verify", 0);
    await Run(candidate, "wrong-service", 75);
    var correctKey = environment["REHEARSAL_KEY"];
    environment["REHEARSAL_KEY"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await Run(candidate, "wrong-key", 74);
    environment["REHEARSAL_KEY"] = correctKey;

    // Every writer has exited. This account belongs exclusively to this synthetic rehearsal.
    // Restore all blob containers, including removing cookie keys and the new binding marker.
    await foreach (var item in blobService.GetBlobContainersAsync(cancellationToken: ct))
    { await blobService.DeleteBlobContainerAsync(item.Name, cancellationToken: ct); }
    foreach (var (name, contents) in backup)
    {
        var container = blobService.GetBlobContainerClient(name);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        foreach (var (blob, bytes) in contents)
        { await container.GetBlobClient(blob).UploadAsync(new BinaryData(bytes), cancellationToken: ct); }
    }
    await Run(legacy, "verify", 0);
    await app.StopAsync(ct);
}
volume.Complete();
await File.WriteAllTextAsync(Path.Combine(directory, "rehearsal-passed.json"), JsonSerializer.Serialize(new
{
    LegacyRevision = "e39ecb35df23b80ef2f9340ea80a8bd6c8f8e3a5",
    Stages = new[] { "packed-composition", "two-brain-isolation", "resource-rename-restart", "memory-composition", "changed-service-detected", "readonly-preflight", "checkpoint-interruption", "incomplete-startup-blocked", "process-interruption", "migration-resume", "revocation-restart", "key-reuse", "wrong-key-rejected", "snapshot-rollback-old-reader" }
}), ct);
Console.WriteLine("Persistent upgrade rehearsal passed.");

async Task<SortedDictionary<string, SortedDictionary<string, byte[]>>> Snapshot(BlobServiceClient blobService)
{
    var result = new SortedDictionary<string, SortedDictionary<string, byte[]>>(StringComparer.Ordinal);
    await foreach (var item in blobService.GetBlobContainersAsync(cancellationToken: ct))
    {
        var container = blobService.GetBlobContainerClient(item.Name);
        var contents = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
        { contents.Add(blob.Name, (await container.GetBlobClient(blob.Name).DownloadContentAsync(ct)).Value.Content.ToArray()); }
        result.Add(item.Name, contents);
    }
    return result;
}

async Task Run(string assembly, string mode, int? expectedExit)
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(assembly);
    start.ArgumentList.Add(mode);
    foreach (var (name, value) in environment) { start.Environment[name] = value; }
    using var process = Process.Start(start) ?? throw new IOException("Worker did not start.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    try { await process.WaitForExitAsync(ct); }
    catch { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
    await File.WriteAllTextAsync(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(assembly)}-{mode}-{expectedExit?.ToString() ?? "failure"}.log"), await stdout + await stderr, ct);
    if (expectedExit is { } expected ? process.ExitCode != expected : process.ExitCode == 0)
    { throw new InvalidOperationException($"{mode} exited with {process.ExitCode}; expected {expectedExit?.ToString() ?? "failure"}. See rehearsal logs."); }
}
