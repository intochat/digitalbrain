using DigitalBrain.Context;

namespace DigitalBrain.Ino;

/// Ino-owned implementation of capability recall (delegates to Context grain).
/// Lives in the Ino integration (no Kernel dep).
public sealed class InoCapabilityRecall(IGrainFactory grains) : IInoCapabilityRecall
{
    public async Task<IReadOnlyList<string>> RecallAsync(string prompt, int top = 5, CancellationToken cancellationToken = default)
    {
        var context = grains.GetGrain<IContextNeuron>("context-main");
        return await context.RecallAsync(prompt, top);
    }
}
