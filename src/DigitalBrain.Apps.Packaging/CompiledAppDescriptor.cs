using System.Collections.Immutable;

namespace DigitalBrain.Apps.Packaging;

/// <summary>Portable, frozen metadata. HostMetadata stores explicit host extension JSON as strings.</summary>
public sealed record CompiledAppDescriptor
{
    public int SchemaVersion { get; init; } = 1;
    public int HostAbi { get; init; } = 1;
    public string TargetFramework { get; init; } = "net11.0";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string EntryAssembly { get; init; }
    public required string EntryType { get; init; }
    public string FrameworkCompatibility { get; init; } = "1";
    public ImmutableArray<string> Capabilities { get; init; } = [];
    public ImmutableArray<CompiledNeuronDescriptor> Neurons { get; init; } = [];
    public ImmutableArray<CompiledContractDescriptor> Contracts { get; init; } = [];
    public ImmutableArray<CompiledContractDescriptor> Signals { get; init; } = [];
    public ImmutableArray<string> RequiredHostAdapters { get; init; } = [];
    public ImmutableSortedDictionary<string, string> Presentation { get; init; } = ImmutableSortedDictionary<string, string>.Empty;
    public ImmutableSortedDictionary<string, string> HostMetadata { get; init; } = ImmutableSortedDictionary<string, string>.Empty;
    public ImmutableSortedDictionary<string, string> SharedAssemblies { get; init; } = ImmutableSortedDictionary<string, string>.Empty;
    public ImmutableArray<PackagePayload> Payload { get; init; } = [];
}

public sealed record CompiledNeuronDescriptor(string TypeName, string NeuronType, string StateName, string HostPolicy, CompiledContractDescriptor? State)
{
    public ImmutableArray<LegacyNeuronOwnership> LegacyOwnership { get; init; } = [];
}
public sealed record CompiledContractDescriptor(string TypeName, string SerializerIdentity, string SchemaVersion)
{
    public ImmutableArray<CompiledFieldDescriptor> Fields { get; init; } = [];
}
public sealed record CompiledFieldDescriptor(uint Id, string Name, string TypeName, bool Required, string? DefaultJson = null);
public sealed record LegacyNeuronOwnership(string Kind, string Value);
public sealed record PackagePayload(string Path, long Length, string Sha256);
public sealed record ValidatedAppPackage(CompiledAppDescriptor Descriptor, string Digest, string DirectoryPath);

public sealed record PackageLimits
{
    public long MaxArchiveBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxEntries { get; init; } = 4096;
    public long MaxManifestBytes { get; init; } = 1024 * 1024;
}
