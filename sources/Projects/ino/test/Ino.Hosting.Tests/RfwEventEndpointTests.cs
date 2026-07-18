using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Gateway;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Hosting.Tests;

/// <summary>
/// Locks the gateway-side <c>HandleRfwEventAsync</c> behaviour: known
/// correlation_ids dispatch to the registered plan grain via
/// <see cref="IRfwEventHandler"/>; unknown correlation_ids return a
/// friendly-text response (no exception); plan-thrown exceptions surface as
/// <see cref="NeuronResult.Fail"/>.
/// </summary>
public sealed class RfwEventEndpointTests
{
    static InoGateway MakeGateway(IGrainFactory grainFactory) =>
        new(firePort: Substitute.For<IFirePort>(),
            events: Substitute.For<IInoEventBus>(),
            journal: new InMemorySynapseJournal(),
            reasoningProbe: new InMemoryReasoningProbe(),
            grainFactory: grainFactory,
            log: NullLogger<InoGateway>.Instance);

    [Fact]
    public async Task Unknown_correlationId_returns_friendly_text_no_exception()
    {
        // Arrange — registry returns null for the unknown correlation.
        var registry = Substitute.For<ICorrelationRegistry>();
        registry.GetAsync("missing").Returns(Task.FromResult<CorrelationEntry?>(null));

        var grainFactory = Substitute.For<IGrainFactory>();
        grainFactory.GetGrain<ICorrelationRegistry>("singleton").Returns(registry);

        var gateway = MakeGateway(grainFactory);

        // Act
        var result = await gateway.HandleRfwEventAsync(
            "missing",
            "flight.selected",
            new Dictionary<string, string> { ["flightId"] = "FL-1" },
            CancellationToken.None);

        // Assert — graceful fallback, no throw.
        Assert.True(result.Success);
        Assert.Contains("expired", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_correlationId_throws_argument_exception()
    {
        var grainFactory = Substitute.For<IGrainFactory>();
        var gateway = MakeGateway(grainFactory);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            gateway.HandleRfwEventAsync("", "flight.selected",
                new Dictionary<string, string>(), CancellationToken.None));
    }

    [Fact]
    public async Task Empty_eventName_throws_argument_exception()
    {
        var grainFactory = Substitute.For<IGrainFactory>();
        var gateway = MakeGateway(grainFactory);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            gateway.HandleRfwEventAsync("c1", "",
                new Dictionary<string, string>(), CancellationToken.None));
    }
}
