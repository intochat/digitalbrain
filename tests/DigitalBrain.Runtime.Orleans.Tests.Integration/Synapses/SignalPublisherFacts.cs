using System.Collections.Concurrent;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Streams;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// RT-02 publish half, 04 D-01: reference-only items, published only for appended signals.
public sealed class SignalPublisherFacts
{
    private static readonly NeuronId P = NeuronId.Parse("b1/chat/p");

    private sealed class Received
    {
        public ConcurrentQueue<(StreamId Stream, SignalReference Item)> Items { get; } = new();

        public async Task Watch(IStreamProvider provider, string ns)
        {
            var id = StreamNames.For("b1", ns + ".x");
            await provider.GetStream<SignalReference>(id).SubscribeAsync((item, _) =>
            {
                Items.Enqueue((id, item));
                return Task.CompletedTask;
            });
        }

        public async Task WaitFor(int count, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (Items.Count < count && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
        }
    }

    private static async Task<(IInProcessHost Host, SignalPublisher Publisher, PublishFaults Faults, Received Received)> Start(TempHome home, CancellationToken ct)
    {
        var host = await new LocalSiloHostFactory().Start(new InProcessHostOptions { Home = home.Path }, ct);
        var faults = new PublishFaults();
        var publisher = new SignalPublisher(host.Services, faults);
        var received = new Received();
        var provider = host.Services.GetRequiredKeyedService<IStreamProvider>(IntoChatOSNames.SignalStreams);
        await received.Watch(provider, "files");
        await received.Watch(provider, "chat");
        return (host, publisher, faults, received);
    }

    [Fact]
    public async Task Publish_sends_one_reference_per_namespace_with_the_highest_sequence()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var (host, publisher, _, received) = await Start(home, ct);
        await using var _h = host;

        await publisher.Publish(new AppendedSignals(P, [(3, "files.listed"), (4, "chat.sent"), (5, "files.removed")]));
        await received.WaitFor(2, ct);

        Assert.Equal(2, received.Items.Count);
        Assert.Contains(received.Items, r => r.Stream.GetNamespace() == "brain.signals.files" && r.Item == new SignalReference(P, 5));
        Assert.Contains(received.Items, r => r.Stream.GetNamespace() == "brain.signals.chat" && r.Item == new SignalReference(P, 4));
    }

    [Fact]
    public async Task Streams_are_keyed_by_the_publisher_brain()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var (host, publisher, _, received) = await Start(home, ct);
        await using var _h = host;

        await publisher.Publish(new AppendedSignals(P, [(1, "files.listed")]));
        await received.WaitFor(1, ct);

        Assert.Equal("b1", Assert.Single(received.Items).Stream.GetKeyAsString());
    }

    [Fact]
    public async Task Announce_republishes_the_publisher_last_on_each_namespace()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var (host, publisher, _, received) = await Start(home, ct);
        await using var _h = host;

        await publisher.Announce(P, 9, ["files", "chat", "files"]);
        await received.WaitFor(2, ct);

        Assert.Equal(2, received.Items.Count);
        Assert.All(received.Items, r => Assert.Equal(new SignalReference(P, 9), r.Item));
    }

    [Fact]
    public async Task With_FailAfterAppend_Publish_throws_and_sends_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var (host, publisher, faults, received) = await Start(home, ct);
        await using var _h = host;
        faults.FailAfterAppend = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.Publish(new AppendedSignals(P, [(1, "files.listed")])));
        await Task.Delay(500, ct);

        Assert.Empty(received.Items);
    }
}
