using DigitalBrain.Protocol;
using DigitalBrain.Os.Domain.Events;

namespace DigitalBrain.Os.Application;

public interface IUiNeuron : INeuron,
    IHandle<NeuronTelemetry>
{
    Task<UiState> GetStateAsync(CancellationToken cancellationToken = default);
    Task RegisterTelemetryAsync(string evt, Dictionary<string, string> data, CancellationToken cancellationToken = default);
    Task SwitchBrainAsync(string brainId, CancellationToken cancellationToken = default);
    Task AddBrainAsync(string brainId, CancellationToken cancellationToken = default);
}

[GenerateSerializer]
public sealed record UiState
{
    [Id(0)]
    public string Username { get; set; } = string.Empty;

    [Id(1)]
    public string CurrentBrainId { get; set; } = "global";

    [Id(2)]
    public string CurrentTab { get; set; } = "0";

    [Id(3)]
    public List<string> AvailableBrains { get; set; } = new() { "global" };

    [Id(4)]
    public List<string> TelemetryLogs { get; set; } = new();
}
