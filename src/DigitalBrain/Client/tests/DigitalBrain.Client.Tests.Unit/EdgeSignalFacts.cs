using System.Net;
using System.Text;
using DigitalBrain.Contracts.Edge.V1;

namespace DigitalBrain.Client.Tests.Unit;

public sealed class EdgeSignalFacts
{
    public sealed record Changed(int Value) : Signal;
    public interface ISource : INeuron { Task Read(); }

    [Fact]
    public async Task OverflowIsVisibleWithoutDrainingTheBuffer()
    {
        var events = string.Concat(Enumerable.Range(0, 1100).Select(i => $"event: {ScriptEdgeProtocol.SignalEvent}\ndata: {{\"value\":{i}}}\n\n"));
        using var http = new HttpClient(new Handler(events)) { BaseAddress = new("https://example.test") };
        var edge = new ScriptEdgeClient(http);
        var source = (NeuronProxy)(object)NeuronProxy.Create<ISource>(edge, "source");
        await using var subscription = await EdgeSignalSubscription<Changed>.OpenAsync(edge, source, TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Contains("overflow", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentDisposalIsSafe()
    {
        using var http = new HttpClient(new Handler("")) { BaseAddress = new("https://example.test") };
        var edge = new ScriptEdgeClient(http);
        var source = (NeuronProxy)(object)NeuronProxy.Create<ISource>(edge, "source");
        var subscription = await EdgeSignalSubscription<Changed>.OpenAsync(edge, source, TestContext.Current.CancellationToken);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => subscription.DisposeAsync().AsTask()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.Completion);
    }

    private sealed class Handler(string events) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") });
    }
}
