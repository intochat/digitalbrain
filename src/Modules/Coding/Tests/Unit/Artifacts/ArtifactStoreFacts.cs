using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ArtifactStoreFacts
{
    [Fact]
    public async Task ActivationRechecksLiveEnvironment()
    {
        await using var fixture = new ArtifactFixture();
        var current = fixture.Environment;
        var store = new ArtifactStore(fixture.Root, () => current);
        var reference = await store.SealAsync(fixture.Build, TestContext.Current.CancellationToken);
        current = current with { Assemblies = new Dictionary<string, string> { ["brain"] = "changed" } };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenVerifiedAsync(reference, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IndependentStoresSerializeQuotaChecks()
    {
        await using var fixture = new ArtifactFixture();
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllBytesAsync(Path.Combine(fixture.Build.PayloadDirectory, "App.dll"), new byte[4096], ct);
        var first = new ArtifactStore(fixture.Root, fixture.Environment, 7000);
        var second = new ArtifactStore(fixture.Root, fixture.Environment, 7000);
        var tasks = new[] { first.SealAsync(fixture.Build, ct), second.SealAsync(fixture.Build with { Source = "other" }, ct) };
        try { await Task.WhenAll(tasks); } catch (IOException) { }
        _ = Assert.Single(tasks, t => t.IsCompletedSuccessfully);
        _ = Assert.Single(tasks, t => t.IsFaulted && t.Exception!.InnerException is IOException);
    }

    [Fact]
    public async Task IndependentStoresDeduplicateConcurrentSeals()
    {
        await using var fixture = new ArtifactFixture();
        var other = new ArtifactStore(fixture.Root, fixture.Environment);
        var results = await Task.WhenAll(fixture.Seal(), other.SealAsync(fixture.Build, TestContext.Current.CancellationToken));
        Assert.Equal(results[0], results[1]);
    }

    [Fact]
    public async Task ChangedPayloadCannotBeOpened()
    {
        await using var fixture = new ArtifactFixture();
        var reference = await fixture.Seal();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, reference.Id, "payload", "App.dll"), "changed", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.OpenVerifiedAsync(reference, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdenticalBuildIsDeduplicated()
    {
        await using var fixture = new ArtifactFixture();
        Assert.Equal(await fixture.Seal(), await fixture.Seal());
    }

    [Fact]
    public async Task UntestedBuildCannotBeSealed()
    {
        await using var fixture = new ArtifactFixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.SealAsync(fixture.Build with { Report = new(0, 0, 0, 0, "report") }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnvironmentChangeInvalidatesArtifact()
    {
        await using var fixture = new ArtifactFixture();
        var reference = await fixture.Seal();
        var changed = new ArtifactStore(fixture.Root, fixture.Environment with { Sdk = "different" });
        await Assert.ThrowsAsync<InvalidDataException>(() => changed.OpenVerifiedAsync(reference, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExtraPayloadFileInvalidatesArtifact()
    {
        await using var fixture = new ArtifactFixture();
        var reference = await fixture.Seal();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, reference.Id, "payload", "Injected.dll"), "extra", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.OpenVerifiedAsync(reference, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PathsCannotBeUsedAsArtifactIds()
    {
        await using var fixture = new ArtifactFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.OpenVerifiedAsync(new("../other", "source", "env"), TestContext.Current.CancellationToken));
    }

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "brain-artifact-tests", Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(_directory, "store");
        public BuildEnvironment Environment { get; } = new("sdk", "windows-x64", "template", "validator-1", new Dictionary<string, string> { ["brain"] = "hash" });
        public ArtifactStore Store { get; }
        public VerifiedBuild Build { get; }
        public ArtifactFixture()
        {
            var payload = Path.Combine(_directory, "candidate");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, "App.dll"), "compiled payload");
            Store = new(Root, Environment);
            Build = new("source", "tests", Environment, payload, "App.dll", new(1, 1, 0, 0, "report"));
        }
        public Task<CodeArtifactRef> Seal() => Store.SealAsync(Build, TestContext.Current.CancellationToken);
        public ValueTask DisposeAsync() { Directory.Delete(_directory, true); return ValueTask.CompletedTask; }
    }
}
