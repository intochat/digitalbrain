using DigitalBrain.Contracts;
using DigitalBrain.Testing;

await using var brain = await DigitalBrainSimulation.StartAsync(new()
{
    Modules = [new TestTwitterModule()],
});
await ElonBitcoin.Run(brain, CancellationToken.None);

public static class ElonBitcoin
{
    public static async Task Run(IDigitalBrain brain, CancellationToken cancellation)
    {
        var elon = brain.Get<ITwitterAccount>("elonmusk");
        var btc = brain.Get<IBitcoin>("btc");
        var ui = brain.Get<INotification>("ui");

        await foreach (var post in brain.On<Posted>(elon, cancellation))
        {
            if (!post.Text.Contains("Bitcoin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var price = await btc.GetPrice();
            await ui.Notify($"{post.From}: {post.Text}  BTC {price:0}");
        }
    }
}
