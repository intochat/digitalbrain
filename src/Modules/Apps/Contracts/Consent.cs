using DigitalBrain.Contracts;
using Orleans.Concurrency;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// One data class the consent sheet names: which semantic type the app touches and why.
[GenerateSerializer, Alias("apps.consent-data-type")]
public sealed record ConsentDataType
{
    [Id(0)] public required string SemanticTypeId { get; init; }
    [Id(1)] public required string Reason { get; init; }
    [Id(2)] public bool Write { get; init; }
}

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

// One consent sheet per workspace, keyed by the workspace scope id.
[Alias("app-consent"), DefaultGrainType("app-consent")]
public interface IAppConsent : INeuron
{
    Task<AppConsentSheet> Review(string appId);

    Task<AppConsentSheet> Approve(string appId);

    [ReadOnly, Alias("is-approved")]
    Task<bool> IsApproved(string appId);
}
