namespace DigitalBrain.Apps;

// Revisions holds the full ancestry of every revision this package references, so any package
// can answer for its own lineage when another one forks, pulls or accepts from it.
[GenerateSerializer, Alias("apps.package-state")]
public sealed record PackageState
{
    [Id(0)] public PackageRevisionRef? ForkedFrom { get; init; }
    [Id(1)] public string? Head { get; init; }
    [Id(2)] public string? Published { get; init; }
    [Id(3)] public Dictionary<string, PackageRevision> Revisions { get; init; } = [];
    [Id(4)] public List<string> History { get; init; } = [];
    [Id(5)] public List<PackageProposal> Proposals { get; init; } = [];
    [Id(6)] public List<OperationReceipt> Receipts { get; init; } = [];
}
