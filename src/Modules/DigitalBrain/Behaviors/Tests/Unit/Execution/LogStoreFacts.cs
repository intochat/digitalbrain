using DigitalBrain.Behavior;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class LogStoreFacts
{
    [Fact]
    public async Task RingReportsTruncationAndSurvivesReopening()
    {
        var documents = new InMemoryDocumentStore<LogDocument>();
        var logs = new BehaviorLogStore(documents, 10);
        var generation = Guid.NewGuid();
        await logs.AppendAsync("p", generation, "stdout", "123456", TestContext.Current.CancellationToken);
        await logs.AppendAsync("p", generation, "stdout", "abcdef", TestContext.Current.CancellationToken);
        var page = await new BehaviorLogStore(documents, 10).ReadAsync("p", 0, 100, TestContext.Current.CancellationToken);
        Assert.True(page.Truncated);
        Assert.Equal("abcdef", Assert.Single(page.Entries).Message);
        Assert.Equal(2, page.LastSequence);
    }
}
