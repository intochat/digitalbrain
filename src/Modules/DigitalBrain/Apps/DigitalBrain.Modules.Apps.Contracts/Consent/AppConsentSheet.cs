namespace DigitalBrain.Apps;

// The sheet a person approves before an app is installed: its examples, the data types it uses,
// its permissions and a shadow Compute estimate. Approval is recorded; install then proceeds.
[GenerateSerializer, Alias("apps.consent-sheet")]
public sealed record AppConsentSheet
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Version { get; init; }
    [Id(2)] public required string Name { get; init; }
    [Id(3)] public required string DescriptionForPeople { get; init; }
    [Id(4)] public IReadOnlyList<string> Examples { get; init; } = [];
    [Id(5)] public IReadOnlyList<ConsentDataType> DataTypes { get; init; } = [];
    [Id(6)] public IReadOnlyList<AppMeter> Meters { get; init; } = [];
    [Id(7)] public decimal EstimatedCompute { get; init; }
    [Id(8)] public bool Approved { get; init; }
}
