using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Tests.Fixtures;
using Xunit;

namespace Ino.Core.Hosting.Tests;

[Collection(nameof(InoNeuronTestCollection))]
public sealed class InoNeuronTests
{
    private readonly InoNeuronTestSiloFixture _fixture;

    public InoNeuronTests(InoNeuronTestSiloFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AskAsync_delegates_to_ICortexCapability_and_returns_outcome()
    {
        var expected = NeuronResult.Ok("hello from cortex");
        _fixture.StubCortex.NextResult =
            new RoutingResult(expected, RoutingSource.Regex, ScenarioName: "test");

        var grain = _fixture.Cluster.GrainFactory.GetGrain<IInoNeuron>(
            InoNeuronGrainKey.Format("user-1", InoNeuronGrainKey.DefaultSessionId));

        var response = await grain.AskAsync("hi", correlationId: "corr-1", CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("hello from cortex", response.Text);
        Assert.Equal("corr-1", response.CorrelationId);
        Assert.Equal("Regex", response.Source);
    }
}

[CollectionDefinition(nameof(InoNeuronTestCollection))]
public sealed class InoNeuronTestCollection : ICollectionFixture<InoNeuronTestSiloFixture>
{
}
