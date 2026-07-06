using DigitalBrain.Context;
using DigitalBrain.Ino;

namespace DigitalBrain.Kernel.Ino;

public sealed class KernelInoCapabilityRecall(IGrainFactory grains) : IInoCapabilityRecall
{
    public async Task<IReadOnlyList<string>> RecallAsync(string prompt, int top = 5, CancellationToken cancellationToken = default)
    {
        var context = grains.GetGrain<IContextNeuron>("context-main");
        return await context.RecallAsync(prompt, top);
    }
}