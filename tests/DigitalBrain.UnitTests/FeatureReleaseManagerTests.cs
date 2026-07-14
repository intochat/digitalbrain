using DigitalBrain.FeatureHost;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Contracts;
using System.Runtime.CompilerServices;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class FeatureReleaseManagerTests
{
    private static readonly FeatureInstallationId Installation = new("email-summarizer");

    [Fact]
    public async Task Invalid_staged_release_does_not_replace_active_release()
    {
        using var first = FeatureReleaseTestArtifact.Create("sha256:first");
        using var tampered = FeatureReleaseTestArtifact.Create("sha256:tampered");
        var recycle = new RecordingRecycle();
        await using var manager = Manager(recycle);
        await manager.ActivateAsync(Installation, first.Descriptor);
        File.AppendAllText(tampered.ImplementationAssemblyPath, "tampered");

        await Assert.ThrowsAsync<FeatureReleaseValidationException>(
            () => manager.ActivateAsync(Installation, tampered.Descriptor));

        using var lease = manager.Acquire(Installation);
        Assert.Equal(first.Descriptor.Digest, lease.Digest);
        Assert.Equal(0, recycle.Requests);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Release_with_failed_or_skipped_scenarios_is_rejected(
        bool failed,
        bool skipped)
    {
        using var release = FeatureReleaseTestArtifact.Create(
            scenarioFailures: failed ? 1 : 0,
            scenarioSkips: skipped ? 1 : 0);
        await using var manager = Manager(new RecordingRecycle());

        await Assert.ThrowsAsync<FeatureReleaseValidationException>(() =>
            manager.ActivateAsync(Installation, release.Descriptor));
    }

    [Fact]
    public async Task Release_for_another_sdk_version_is_rejected()
    {
        using var release = FeatureReleaseTestArtifact.Create(sdkVersion: "999.0.0.0");
        await using var manager = Manager(new RecordingRecycle());

        await Assert.ThrowsAsync<FeatureReleaseValidationException>(() =>
            manager.ActivateAsync(Installation, release.Descriptor));
    }

    [Fact]
    public async Task Invalid_release_removes_its_host_owned_snapshot()
    {
        using var release = FeatureReleaseTestArtifact.Create(sdkVersion: "999.0.0.0");
        var cache = Path.Combine(Path.GetTempPath(), $"digitalbrain-feature-test-{Guid.NewGuid():N}");
        try
        {
            await using var manager = new FeatureReleaseManager(
                new SingleServiceProvider(new GmailReader()),
                new RecordingRecycle(),
                cache);

            await Assert.ThrowsAsync<FeatureReleaseValidationException>(() =>
                manager.ActivateAsync(Installation, release.Descriptor));

            Assert.Empty(Directory.EnumerateFileSystemEntries(cache));
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_post_load_validation_unloads_without_recycling()
    {
        using var release = FeatureReleaseTestArtifact.Create(featureTypeName: "Missing.Feature");
        var recycle = new RecordingRecycle();
        await using var manager = Manager(recycle);

        await Assert.ThrowsAsync<FeatureReleaseValidationException>(() =>
            manager.ActivateAsync(Installation, release.Descriptor));

        Assert.Equal(0, recycle.Requests);
    }

    [Fact]
    public async Task Switch_routes_new_leases_immediately_and_waits_for_old_lease_to_drain()
    {
        using var first = FeatureReleaseTestArtifact.Create("sha256:first");
        using var second = FeatureReleaseTestArtifact.Create("sha256:second");
        await using var manager = Manager(new RecordingRecycle());
        await manager.ActivateAsync(Installation, first.Descriptor);
        var oldLease = manager.Acquire(Installation);

        var switchTask = manager.ActivateAsync(Installation, second.Descriptor);
        await WaitUntilAsync(() => manager.GetActiveDigest(Installation) == second.Descriptor.Digest);
        using var newLease = manager.Acquire(Installation);

        Assert.Equal(second.Descriptor.Digest, newLease.Digest);
        Assert.False(switchTask.IsCompleted);
        oldLease.Dispose();
        await switchTask;
    }

    [Fact]
    public async Task Draining_release_does_not_block_unrelated_activation()
    {
        using var first = FeatureReleaseTestArtifact.Create("sha256:first");
        using var second = FeatureReleaseTestArtifact.Create("sha256:second");
        var otherInstallation = new FeatureInstallationId("other-installation");
        await using var manager = Manager(new RecordingRecycle());
        await manager.ActivateAsync(Installation, first.Descriptor);
        var oldLease = manager.Acquire(Installation);

        var switchTask = manager.ActivateAsync(Installation, second.Descriptor);
        await WaitUntilAsync(() => manager.GetActiveDigest(Installation) == second.Descriptor.Digest);
        var unrelated = manager.ActivateAsync(otherInstallation, second.Descriptor);

        await unrelated.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(switchTask.IsCompleted);
        oldLease.Dispose();
        await switchTask;
    }

    [Fact]
    public async Task Restart_reload_and_rollback_restore_exact_releases()
    {
        using var first = FeatureReleaseTestArtifact.Create("sha256:first");
        using var second = FeatureReleaseTestArtifact.Create("sha256:second");
        await using (var manager = Manager(new RecordingRecycle()))
        {
            await manager.ActivateAsync(Installation, first.Descriptor);
        }

        await using var restarted = Manager(new RecordingRecycle());
        await restarted.LoadActiveAsync([
            new FeatureActiveInstallation(Installation, second.Descriptor)
        ]);
        Assert.Equal(second.Descriptor.Digest, restarted.GetActiveDigest(Installation));

        await restarted.ActivateAsync(Installation, first.Descriptor);
        using var lease = restarted.Acquire(Installation);
        Assert.Equal(first.Descriptor.Digest, lease.Digest);
    }

    [Fact]
    public async Task Concurrent_installations_share_one_staged_release_context()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var services = new CountingServiceProvider(new GmailReader());
        await using var manager = new FeatureReleaseManager(services, new RecordingRecycle());
        var installations = Enumerable.Range(0, 32)
            .Select(index => new FeatureInstallationId($"email-summarizer-{index}"))
            .ToArray();

        await Task.WhenAll(installations.Select(installation =>
            manager.ActivateAsync(installation, release.Descriptor)));

        Assert.Equal(1, services.Resolutions);
        using var lease = manager.Acquire(installations[0]);
        Assert.Equal(release.Descriptor.Digest, lease.Digest);
        Assert.Equal(2, services.Resolutions);
    }

    [Fact]
    public async Task Same_installation_id_is_isolated_by_owner()
    {
        using var first = FeatureReleaseTestArtifact.Create("sha256:first");
        using var second = FeatureReleaseTestArtifact.Create("sha256:second");
        var ownerOne = new BrainOwnerId("owner-one");
        var ownerTwo = new BrainOwnerId("owner-two");
        await using var manager = Manager(new RecordingRecycle());

        await manager.LoadActiveAsync([
            new FeatureActiveInstallation(ownerOne, Installation, first.Descriptor),
            new FeatureActiveInstallation(ownerTwo, Installation, second.Descriptor)
        ]);

        using var firstLease = manager.Acquire(ownerOne, Installation, first.Descriptor.Digest);
        using var secondLease = manager.Acquire(ownerTwo, Installation, second.Descriptor.Digest);
        Assert.Equal(first.Descriptor.Digest, firstLease.Digest);
        Assert.Equal(second.Descriptor.Digest, secondLease.Digest);
    }

    [Fact]
    public async Task Leaked_feature_requests_recycle_when_retired_context_cannot_unload()
    {
        using var first = FeatureReleaseTestArtifact.Create("sha256:first");
        using var second = FeatureReleaseTestArtifact.Create("sha256:second");
        var recycle = await CauseFailedUnload(first.Descriptor, second.Descriptor);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(1, recycle.Requests);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<RecordingRecycle> CauseFailedUnload(
        FeatureReleaseDescriptor first,
        FeatureReleaseDescriptor second)
    {
        var recycle = new RecordingRecycle();
        await using var manager = Manager(recycle);
        await manager.ActivateAsync(Installation, first);
        var lease = manager.Acquire(Installation);
        var leaked = lease.Feature;
        lease.Dispose();

        await manager.ActivateAsync(Installation, second);

        Assert.NotNull(leaked);
        GC.KeepAlive(leaked);
        return recycle;
    }

    private static FeatureReleaseManager Manager(RecordingRecycle recycle) =>
        new(new SingleServiceProvider(new GmailReader()), recycle);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, cancellation.Token);
    }

    private sealed class SingleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
    }

    private sealed class CountingServiceProvider(object service) : IServiceProvider
    {
        private int _resolutions;
        public int Resolutions => Volatile.Read(ref _resolutions);

        public object? GetService(Type serviceType)
        {
            if (!serviceType.IsInstanceOfType(service))
                return null;
            Interlocked.Increment(ref _resolutions);
            return service;
        }
    }

    private sealed class GmailReader : IGmailMessageReader
    {
        public Task<GmailMessage> ReadAsync(
            GmailMessageReadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GmailMessage(
                request.MessageId,
                null,
                DateTimeOffset.UnixEpoch,
                null,
                "subject",
                "body"));
    }

    private sealed class RecordingRecycle : IFeatureHostRecycle
    {
        public int Requests { get; private set; }
        public void RequestRecycle() => Requests++;
    }
}
