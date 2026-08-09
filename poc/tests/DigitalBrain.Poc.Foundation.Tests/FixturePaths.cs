using System.IO;

namespace DigitalBrain.Poc.Foundation.Tests;

internal static class FixturePaths
{
    public static string ProbeNeuron { get; } = Path.Combine(
        PocPaths.Root,
        "tests",
        "DigitalBrain.Poc.Foundation.Tests",
        "Fixtures",
        "probe-neuron.cs");
}
