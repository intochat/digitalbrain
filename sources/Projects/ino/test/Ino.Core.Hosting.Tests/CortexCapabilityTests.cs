using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Capabilities;
using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class CortexCapabilityTests
{
    [Fact]
    public async Task RouteAsync_returns_unrouted_when_no_neurons_installed()
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(Array.Empty<INeuronDefinition>()));

        var firePort = Substitute.For<IFirePort>();
        var chat = Substitute.For<IChatClient>();
        var corpus = Substitute.For<INeuronPromptCorpus>();
        corpus.Count.Returns(0);
        var grainFactory = Substitute.For<IGrainFactory>();

        var capability = new CortexCapability(
            discovery,
            firePort,
            chat,
            corpus,
            grainFactory,
            NullLogger<CortexCapability>.Instance);

        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("test")),
            SourceStream: new StreamKey("<test>"),
            UserId: "user-1")
        {
            FirePort = firePort,
            Logger = NullLogger.Instance,
        };

        var result = await capability.RouteAsync("anything", ctx, CancellationToken.None);

        Assert.Equal(RoutingSource.Unrouted, result.Source);
        Assert.True(result.Outcome.Success);
        Assert.Contains("No specialist", result.Outcome.Message);
    }
}
