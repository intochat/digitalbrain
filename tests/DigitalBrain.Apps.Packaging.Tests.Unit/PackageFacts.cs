using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DigitalBrain.Apps.Packaging;
using DigitalBrain.Capabilities;
using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Packaging.Tests.Unit;

public sealed class PackageFacts : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "packages-" + Guid.NewGuid().ToString("N"));

    public PackageFacts() => Directory.CreateDirectory(root);

    [Fact]
    public void TrustedBuildMetadataFreezesTypedManifestAndSchemas()
    {
        var manifest = new AppManifest("example.app", "Example", "2.0.0", [typeof(OwnedNeuron)], new([new CapabilityId("storage", 1)]));
        var metadata = TrustedBuildMetadata.Extract(typeof(ExampleApp), manifest, Descriptor() with
        {
            HostMetadata = System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty.Add("intochat.os.v1", "{\"operations\":[\"open\"]}")
        });
        Assert.Equal("2.0.0", metadata.Version);
        Assert.Equal(["storage@1"], metadata.Capabilities);
        Assert.Equal("{\"operations\":[\"open\"]}", metadata.HostMetadata["intochat.os.v1"]);
        var neuron = Assert.Single(metadata.Neurons);
        Assert.Equal("example-items", neuron.NeuronType);
        Assert.Equal("old-state", neuron.StateName);
        Assert.Equal("affine", neuron.HostPolicy);
        Assert.NotNull(neuron.State);
        Assert.Equal("state-v1", neuron.State.SerializerIdentity);
        Assert.Equal("Count", Assert.Single(neuron.State.Fields).Name);
        Assert.Equal("System.Int32", Assert.Single(neuron.State.Fields).TypeName);
        Assert.Equal(3u, Assert.Single(neuron.State.Fields).Id);
    }

    [Fact]
    public void TrustedBuildMetadataRefusesIdentityAndCapabilityWidening()
    {
        var manifest = new AppManifest("different", "Example", "1", [], new([]));
        Assert.Throws<InvalidDataException>(() => TrustedBuildMetadata.Extract(typeof(ExampleApp), manifest));
        manifest = manifest with { Id = "example.app" };
        Assert.Throws<InvalidDataException>(() => TrustedBuildMetadata.Extract(typeof(ExampleApp), manifest, Descriptor() with { Capabilities = ["storage@1"] }));
    }

    [Fact]
    public void TrustedBuildMetadataSupportsStatelessNeuronsWithoutInventedState()
    {
        var manifest = new AppManifest("example.app", "Example", "1", [typeof(StatelessOwnedNeuron)], new([]));
        var neuron = Assert.Single(TrustedBuildMetadata.Extract(typeof(ExampleApp), manifest).Neurons);
        Assert.Equal("stateless", neuron.HostPolicy);
        Assert.Equal("state", neuron.StateName);
        Assert.Null(neuron.State);
    }

    [Fact]
    public async Task WriterExcludesSourcesSecretsAndSharedContracts()
    {
        var build = Build();
        File.WriteAllText(Path.Combine(build, "source.cs"), "source");
        File.WriteAllText(Path.Combine(build, "appsettings.json"), "secret");
        File.WriteAllText(Path.Combine(build, "debug.pdb"), "symbols");
        File.Copy(typeof(App).Assembly.Location, Path.Combine(build, "DigitalBrain.dll"));
        var shared = System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty.Add("DigitalBrain", typeof(App).Assembly.GetName().Version!.ToString());
        using var archive = new MemoryStream();
        await new AppPackageWriter().Write(Descriptor() with { SharedAssemblies = shared }, build, archive, TestContext.Current.CancellationToken);
        archive.Position = 0;
        var package = await new AppPackageReader(sharedAssemblies: shared).Read(archive, Path.Combine(root, "accepted"), TestContext.Current.CancellationToken);
        Assert.Equal(2, package.Descriptor.Payload.Length);
        Assert.False(File.Exists(Path.Combine(package.DirectoryPath, "DigitalBrain.dll")));
    }

    [Fact]
    public async Task PackageRoundTripHasStableDigest()
    {
        var build = Build();
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        var writer = new AppPackageWriter();
        var digest = await writer.Write(Descriptor(), build, first, TestContext.Current.CancellationToken);
        Assert.Equal(digest, await writer.Write(Descriptor(), build, second, TestContext.Current.CancellationToken));
        Assert.Equal(first.ToArray(), second.ToArray());
        first.Position = 0;
        var package = await new AppPackageReader().Read(first, Path.Combine(root, "accepted"), TestContext.Current.CancellationToken);
        Assert.Equal(digest, package.Digest);
        Assert.Equal("example.app", package.Descriptor.Id);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(package.DirectoryPath, "assets/hello.txt"), TestContext.Current.CancellationToken));
        Assert.Equal(2, package.Descriptor.Payload.Length);
    }

    [Fact]
    public async Task WriterAndReaderAgreeOnExpandedBoundaryIncludingManifest()
    {
        var build = Build();
        using var baseline = new MemoryStream();
        await new AppPackageWriter().Write(Descriptor(), build, baseline, TestContext.Current.CancellationToken);
        baseline.Position = 0;
        long expanded;
        using (var zip = new ZipArchive(baseline, ZipArchiveMode.Read, true)) { expanded = zip.Entries.Sum(entry => entry.Length); }

        using var rejected = new MemoryStream();
        var belowBoundary = new PackageLimits { MaxExpandedBytes = expanded - 1 };
        await Assert.ThrowsAsync<InvalidDataException>(() => new AppPackageWriter(belowBoundary).Write(Descriptor(), build, rejected, TestContext.Current.CancellationToken));
        Assert.Equal(0, rejected.Length);

        var atBoundary = new PackageLimits { MaxExpandedBytes = expanded };
        using var accepted = new MemoryStream();
        var digest = await new AppPackageWriter(atBoundary).Write(Descriptor(), build, accepted, TestContext.Current.CancellationToken);
        accepted.Position = 0;
        var package = await new AppPackageReader(atBoundary).Read(accepted, Path.Combine(root, "accepted"), TestContext.Current.CancellationToken);
        Assert.Equal(digest, package.Digest);
    }

    [Fact]
    public async Task WriterRejectsEntryAssemblyCaseMismatch()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AppPackageWriter().Write(Descriptor() with { EntryAssembly = "EXAMPLE.dll" }, Build(), output, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task ReaderRejectsEntryAssemblyCaseMismatchBeforeStaging()
    {
        using var archive = await Archive();
        Mutate(archive, zip =>
        {
            var entry = zip.GetEntry("app.json")!;
            string json;
            using (var reader = new StreamReader(entry.Open())) { json = reader.ReadToEnd(); }
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("app.json").Open(), new UTF8Encoding(false));
            writer.Write(json.Replace("\"entryAssembly\":\"example.dll\"", "\"entryAssembly\":\"EXAMPLE.dll\"", StringComparison.Ordinal));
        });
        await Reject(archive);
    }

    [Fact]
    public async Task InspectDoesNotExecuteEntryType()
    {
        using var archive = new MemoryStream();
        var descriptor = Descriptor() with { EntryType = typeof(EntryWithInitializer).FullName! };
        await new AppPackageWriter().Write(descriptor, Build(), archive, TestContext.Current.CancellationToken);
        archive.Position = 0;
        await new AppPackageReader().Read(archive, Path.Combine(root, "accepted"), TestContext.Current.CancellationToken);
        Assert.Null(Environment.GetEnvironmentVariable("DIGITALBRAIN_PACKAGE_SENTINEL"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("assets\\escape")]
    [InlineData("assets/HELLO.txt")]
    [InlineData("assets/hello.txt")]
    [InlineData("assets")]
    public async Task RejectsUnsafeOrDuplicatePaths(string path)
    {
        using var archive = await Archive();
        Mutate(archive, zip => WriteEntry(zip, path, "bad"));
        await Reject(archive);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("changed")]
    public async Task RejectsMissingExtraOrChangedPayload(string change)
    {
        using var archive = await Archive();
        Mutate(archive, zip =>
        {
            if (change != "extra") { zip.GetEntry("assets/hello.txt")!.Delete(); }
            if (change != "missing") { WriteEntry(zip, change == "extra" ? "extra.txt" : "assets/hello.txt", "tampered"); }
        });
        await Reject(archive);
    }

    [Fact]
    public async Task EnforcesExpandedLimits()
    {
        using var archive = await Archive();
        await Reject(archive, new AppPackageReader(new PackageLimits { MaxExpandedBytes = 4 }));
    }

    [Fact]
    public async Task AcceptedArtifactsAreReadOnlyAndReusable()
    {
        using var archive = await Archive();
        var staging = Path.Combine(root, "accepted");
        var package = await new AppPackageReader().Read(archive, staging, TestContext.Current.CancellationToken);
        Assert.True((File.GetAttributes(Path.Combine(package.DirectoryPath, "example.dll")) & FileAttributes.ReadOnly) != 0);
        archive.Position = 0;
        var again = await new AppPackageReader().Read(archive, staging, TestContext.Current.CancellationToken);
        Assert.Equal(package.DirectoryPath, again.DirectoryPath);
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("duplicate-json")]
    [InlineData("null-neuron")]
    [InlineData("null-contract")]
    [InlineData("null-signal")]
    [InlineData("null-payload")]
    [InlineData("null-id")]
    [InlineData("null-field")]
    [InlineData("null-ownership")]
    public async Task RejectsMalformedMetadataAndLinks(string reason)
    {
        using var archive = await Archive();
        Mutate(archive, zip =>
        {
            if (reason == "symlink") { zip.GetEntry("assets/hello.txt")!.ExternalAttributes = 0xA1FF << 16; return; }
            var entry = zip.GetEntry("app.json")!;
            string json;
            using (var reader = new StreamReader(entry.Open())) { json = reader.ReadToEnd(); }
            entry.Delete();
            json = reason switch
            {
                "duplicate-json" => json.Replace("\"hostAbi\":1", "\"hostAbi\":2,\"hostAbi\":1", StringComparison.Ordinal),
                "null-neuron" => json.Replace("\"neurons\":[]", "\"neurons\":[null]", StringComparison.Ordinal),
                "null-contract" => json.Replace("\"contracts\":[]", "\"contracts\":[null]", StringComparison.Ordinal),
                "null-signal" => json.Replace("\"signals\":[]", "\"signals\":[null]", StringComparison.Ordinal),
                "null-payload" => json.Replace("\"payload\":[", "\"payload\":[null,", StringComparison.Ordinal),
                "null-id" => json.Replace("\"id\":\"example.app\"", "\"id\":null", StringComparison.Ordinal),
                "null-field" => json.Replace("\"contracts\":[]", "\"contracts\":[{\"typeName\":\"Example.State\",\"serializerIdentity\":\"state-v1\",\"schemaVersion\":\"1\",\"fields\":[null]}]", StringComparison.Ordinal),
                "null-ownership" => json.Replace("\"neurons\":[]", "\"neurons\":[{\"typeName\":\"Example.Neuron\",\"neuronType\":\"items\",\"stateName\":\"state\",\"hostPolicy\":\"affine\",\"state\":null,\"legacyOwnership\":[null]}]", StringComparison.Ordinal),
                _ => throw new InvalidOperationException("Unknown malformed metadata fixture.")
            };
            WriteEntry(zip, "app.json", json);
        });
        await Reject(archive);
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("manifest")]
    [InlineData("entries")]
    public async Task EnforcesOtherLimits(string limit)
    {
        using var archive = await Archive();
        await Reject(archive, new AppPackageReader(limit switch
        {
            "archive" => new() { MaxArchiveBytes = 32 },
            "manifest" => new() { MaxManifestBytes = 32 },
            _ => new() { MaxEntries = 2 }
        }));
    }

    [Theory]
    [InlineData("abi")]
    [InlineData("native")]
    [InlineData("framework")]
    public async Task RejectsUnsupportedAbiAndNativePayload(string reason)
    {
        using var archive = await Archive();
        Mutate(archive, zip =>
        {
            var entry = zip.GetEntry("app.json")!;
            string json;
            using (var reader = new StreamReader(entry.Open())) { json = reader.ReadToEnd(); }
            entry.Delete();
            if (reason == "abi") { json = json.Replace("\"hostAbi\":1", "\"hostAbi\":2", StringComparison.Ordinal); }
            else if (reason == "framework") { json = json.Replace("\"frameworkCompatibility\":\"1\"", "\"frameworkCompatibility\":\"2\"", StringComparison.Ordinal); }
            else { WriteEntry(zip, "native.so", "native"); }
            WriteEntry(zip, "app.json", json);
        });
        await Reject(archive);
    }

    private async Task Reject(MemoryStream archive, AppPackageReader? reader = null)
    {
        archive.Position = 0;
        var staging = Path.Combine(root, Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<InvalidDataException>(() => (reader ?? new AppPackageReader()).Read(archive, staging, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any());
    }

    private async Task<MemoryStream> Archive()
    {
        var archive = new MemoryStream();
        await new AppPackageWriter().Write(Descriptor(), Build(), archive, TestContext.Current.CancellationToken);
        archive.Position = 0;
        return archive;
    }

    private static CompiledAppDescriptor Descriptor() => new()
    {
        Id = "example.app",
        Name = "Example",
        Version = "1.0.0",
        EntryAssembly = "example.dll",
        EntryType = "Example.App"
    };

    private string Build()
    {
        var directory = Path.Combine(root, "build");
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.Copy(typeof(PackageFacts).Assembly.Location, Path.Combine(directory, "example.dll"), true);
        File.WriteAllText(Path.Combine(directory, "assets/hello.txt"), "hello");
        return directory;
    }

    private static void Mutate(MemoryStream stream, Action<ZipArchive> action)
    {
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update, true);
        action(zip);
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(root, true);
    }

    private sealed class EntryWithInitializer
    {
        static EntryWithInitializer() => Environment.SetEnvironmentVariable("DIGITALBRAIN_PACKAGE_SENTINEL", "executed");
    }

    [App("example.app")]
    private sealed class ExampleApp : App;

    [Neuron("example-items", State = "old-state")]
    private sealed class OwnedNeuron : Neuron<OwnedState>;

    [Neuron("stateless-items"), StatelessNeuron]
    private sealed class StatelessOwnedNeuron : Neuron;

    [NeuronContract, NeuronAlias("state-v1")]
    private sealed record OwnedState
    {
        [Field(3)] public int Count { get; init; }
    }
}
