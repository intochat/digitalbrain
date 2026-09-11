using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.UI;

namespace DigitalBrain.Tests;

// Exercise the real asynchronous delivery boundary with a render slower than the model's reply.
internal sealed class DelayedUiCardFilter(BrainWorld world) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (world.CardDeliveryDelay > TimeSpan.Zero
            && context.InterfaceMethod.Name == nameof(INeuron.Deliver)
            && context.Request.GetArgument(0) is SignalDelivery delivery
            && delivery.Signal.Type is UIVocabulary.ChartRendered or UIVocabulary.GraphRendered or UIVocabulary.ImageDescribed)
        {
            await Task.Delay(world.CardDeliveryDelay);
        }

        await context.Invoke();
    }
}
