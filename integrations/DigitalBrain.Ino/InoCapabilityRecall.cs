using DigitalBrain.Ino.Context;

namespace DigitalBrain.Ino;

/// Ino-owned implementation of capability recall (delegates to context grain).
/// Lives in the Ino integration.
public sealed class InoCapabilityRecall(IGrainFactory grains) : IInoCapabilityRecall
{
    public async Task<IReadOnlyList<string>> RecallAsync(string prompt, int top = 5, CancellationToken cancellationToken = default)
    {
        var context = grains.GetGrain<IContextNeuron>("context-main");
        return await context.RecallAsync(prompt, top);
    }
}
