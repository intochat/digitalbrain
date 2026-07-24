using DigitalBrain.Client;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class ClientAndOwnerContracts(TestingFixture fixture)
{
    [Fact]
    public async Task ClientIsTheProductionContract()
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        Assert.IsType<DigitalBrainClient>(test.Client);
        Assert.Equal("hello", await test.Client.Get<IEchoNeuron>().Echo("hello"));
    }

    [Fact]
    public async Task LogicalOwnersAreScopedToTheMethod()
    {
        string firstAlice;
        await using (var first =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken))
        {
            var alice = first.Owner("alice");
            var bob = first.Owner("bob");

            firstAlice = alice.Id.Value;
            Assert.NotEqual(alice.Id, bob.Id);
            Assert.NotSame(alice.Client, bob.Client);
            Assert.Equal(alice.Id, alice.Client.Owner);
        }

        await using var second =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(firstAlice, second.Owner("alice").Id.Value);
    }

    [Fact]
    public async Task DefaultOwnerReusesTheDefaultClientIdentity()
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        var owner = test.Owner("default");

        Assert.Same(test.Client, owner.Client);
        Assert.Equal(owner.Id, test.Client.Owner);
    }

    [Fact]
    public async Task OwnersAreCachedByExactOrdinalLabel()
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        var first = test.Owner("Alice");
        var second = test.Owner("Alice");

        Assert.Same(first, second);
        Assert.Same(first.Client, second.Client);
        Assert.IsType<DigitalBrainClient>(first.Client);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("two owners")]
    [InlineData("owner/name")]
    public async Task InvalidOwnerLabelsAreRejected(string label)
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        var failure = Assert.Throws<BrainTestFailureException>(
            () => test.Owner(label));
        Assert.IsAssignableFrom<ArgumentException>(
            failure.InnerException);
    }

    [Fact]
    public async Task OwnerLabelsRejectACaseCollision()
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        test.Owner("Alice");

        var failure = Assert.Throws<BrainTestFailureException>(
            () => test.Owner("alice"));
        var collision = Assert.IsType<ArgumentException>(
            failure.InnerException);

        Assert.Contains("Alice", collision.Message, StringComparison.Ordinal);
        Assert.Contains("alice", collision.Message, StringComparison.Ordinal);
    }
}
