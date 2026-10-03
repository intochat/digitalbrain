using DigitalBrain.Contracts.Edge.V1;
namespace DigitalBrain.Client;

internal static class ScriptSignalQuery
{
    public static string SignalsQuery(string contract, string key, string signal)
        => $"{DigitalBrain.Contracts.Edge.V1.ScriptEdgeProtocol.Signals}?contract={Uri.EscapeDataString(contract)}&key={Uri.EscapeDataString(key)}&signal={Uri.EscapeDataString(signal)}";
}
