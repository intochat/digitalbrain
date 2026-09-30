using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Runtime.Tests.Unit.Brain;

public sealed class BrainFacts
{
    [Fact]
    public async Task EstablishIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await UnitTest.Create().StartAsync(ct);
        var neuron = host.Get<IBrain>(BrainScope.Create("acct", "personal").Id);
        var first = await neuron.Establish(new("personal", "acct"));
        var second = await neuron.Establish(new("personal", "acct"));
        Assert.Equal("personal", first.Name);
        Assert.Equal("acct", first.OwnerAccountId);
        Assert.Equal(first.EstablishedAt, second.EstablishedAt);
    }
}
