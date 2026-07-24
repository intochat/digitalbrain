using Xunit;

namespace DigitalBrain.HostTests;

public sealed class HostedResourceContracts(TestingAppHostFixture fixture)
{
    [Fact]
    public async Task ResourceBindsItsNameOnceAndWaitsForHealth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var silo = host.Resource("silo");

        await silo.WaitUntilHealthyAsync(cancellationToken);
        using var client = silo.CreateHttpClient();
        using var response = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            cancellationToken);

        Assert.Equal("silo", silo.Name);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task UnknownResourceFailsWithSortedKnownResourceNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);

        var failure = Assert.Throws<InvalidOperationException>(
            () => host.Resource("missing"));

        Assert.Contains("missing", failure.Message, StringComparison.Ordinal);

        const string prefix = "Known resources: ";
        var listStart = failure.Message.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(listStart >= 0, failure.Message);

        var knownNames = failure.Message[(listStart + prefix.Length)..]
            .TrimEnd('.')
            .Split(", ", StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("silo", knownNames, StringComparer.Ordinal);
        Assert.Equal(
            knownNames.Order(StringComparer.Ordinal),
            knownNames);
    }

    [Fact]
    public async Task RepeatedResourceLookupReturnsTheSameHandle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);

        var first = host.Resource("silo");
        var second = host.Resource("silo");

        Assert.Same(first, second);
    }
}
