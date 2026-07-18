using DigitalBrain.Runtime;

namespace DigitalBrain.InoLang.Tests;

public static class DigitalBrainTesting
{
    public static async Task<TestDigitalBrain> CreateClusterAsync(Action<TestDigitalBrainOptions>? configure = null)
    {
        return await TestDigitalBrain.StartAsync(configure ?? (o => o.WithMockedLlm()));
    }
}
