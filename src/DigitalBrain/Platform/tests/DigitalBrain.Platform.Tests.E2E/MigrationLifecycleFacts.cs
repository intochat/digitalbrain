using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using DigitalBrain.Testing.E2E;

namespace DigitalBrain.Platform.Tests.E2E;

public sealed class MigrationLifecycleFacts
{
    [Fact(Timeout = 540_000)]
    public async Task MigrationSurvivesProcessInterruptionAndRevocationRemainsDurable()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        var ct = deadline.Token;
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        TestContext.Current.TestOutputHelper!.WriteLine("Diagnostics: " + directory);
        var candidate = Assembly.GetExecutingAssembly().Location;
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var environment = new Dictionary<string, string>
        {
            ["IDENTITY_TEST_KEY"] = key,
            ["IDENTITY_TEST_PLAN"] = Path.Combine(directory, "migration-plan.txt"),
            ["IDENTITY_TEST_KEY_PROOF"] = Path.Combine(directory, "key-proof.txt")
        };
        await using var volume = await DurableStorageVolume.CreateAsync(keepOnFailure: false, cancellationToken: ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "volume.txt"), volume.Name, ct);
        await using (var builder = DistributedApplicationTestingBuilder.Create([]))
        {
            var storage = builder.AddAzureStorage("upgrade-storage").RunAsEmulator(emulator => emulator.WithDataVolume(volume.Name).WithArgs("--silent"));
            var blobs = storage.AddBlobs("upgrade-blobs");
            await using var app = await builder.BuildAsync(ct);
            await app.StartAsync(ct);
            await app.ResourceNotifications.WaitForResourceHealthyAsync(storage.Resource.Name, ct);
            environment["IDENTITY_TEST_STORAGE"] = await app.GetConnectionStringAsync(blobs.Resource.Name, ct) ?? throw new InvalidOperationException("Missing storage connection.");

            await Run(candidate, "seed", 0);
            var blobService = new BlobServiceClient(environment["IDENTITY_TEST_STORAGE"]);
            var backup = await Snapshot(blobService);
            Assert.Equal(2, backup["digitalbrain-v2-state"].Count);
            var snapshot = JsonSerializer.SerializeToUtf8Bytes(backup);
            await File.WriteAllBytesAsync(Path.Combine(directory, "legacy-snapshot.json"), snapshot, ct);
            environment["IDENTITY_TEST_SNAPSHOT"] = Convert.ToHexStringLower(SHA256.HashData(snapshot));
            await Run(candidate, "inspect", 0);
            Assert.Equal(snapshot, JsonSerializer.SerializeToUtf8Bytes(await Snapshot(blobService)));
            await Run(candidate, "checkpoint-interrupt", 72);
            await Run(candidate, "blocked", 76);
            await Run(candidate, "interrupt", 73);
            await Run(candidate, "blocked-after-import", 76);
            await Run(candidate, "migrate", 0);
            await Run(candidate, "revoke", 0);
            await Run(candidate, "retry", 0);
            await Run(candidate, "verify", 0);
            await Run(candidate, "wrong-service", 75);
            var correctKey = environment["IDENTITY_TEST_KEY"];
            environment["IDENTITY_TEST_KEY"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await Run(candidate, "wrong-key", 74);
            environment["IDENTITY_TEST_KEY"] = correctKey;

            // Every writer has exited. This account belongs exclusively to this synthetic integration test.
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
            Assert.Equal(snapshot, JsonSerializer.SerializeToUtf8Bytes(await Snapshot(blobService)));
            await Run(candidate, "rollback", 0);
            await app.StopAsync(ct);
        }
        volume.Complete();
        Directory.Delete(directory, recursive: true);
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

        async Task Run(string assembly, string mode, int expectedExit)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("--identity-worker");
            start.ArgumentList.Add(mode);
            foreach (var (name, value) in environment) { start.Environment[name] = value; }
            using var process = Process.Start(start) ?? throw new IOException("Worker did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(ct); }
            catch { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
            finally { await File.WriteAllTextAsync(Path.Combine(directory, mode + ".log"), await stdout + await stderr, CancellationToken.None); }
            Assert.True(process.ExitCode == expectedExit, $"{mode} exited with {process.ExitCode}; expected {expectedExit}. See {directory}.");
        }

    }
}
