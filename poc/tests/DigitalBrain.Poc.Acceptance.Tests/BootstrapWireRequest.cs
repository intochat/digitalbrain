namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record BootstrapWireRequest(
    string PocRoot,
    string RunId,
    IReadOnlyDictionary<string, string> Sessions,
    CandidateModuleWire[] Modules,
    TrustedChartWire[] TrustedCharts);
