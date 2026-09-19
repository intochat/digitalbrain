using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Testing;

namespace DigitalBrain.Behaviors;

public sealed class ElonBitcoin(IDigitalBrain brain) : IBehavior
{
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var elon = brain.Get<ITwitterAccount>("elonmusk");
        var btc = brain.Get<IBitcoin>("btc");
        var ui = brain.Get<INotification>("ui");
        await foreach (var post in brain.On<Posted>(elon, cancellation))
        {
            if (!post.Text.Contains("Bitcoin", StringComparison.OrdinalIgnoreCase)) { continue; }
            var price = await btc.GetPrice();
            await ui.Notify($"{post.From}: {post.Text}  BTC {price:0}");
        }
    }
}
