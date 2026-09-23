using DigitalBrain.Behavior;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class LogStoreFacts
{
    [Fact]
    public async Task RingReportsTruncationAndSurvivesReopening()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-logs", Guid.NewGuid().ToString("N"));
        try
        {
            var logs = new BehaviorLogStore(root, 10);
            var generation = Guid.NewGuid();
            await logs.AppendAsync("p", generation, "stdout", "123456", TestContext.Current.CancellationToken);
            await logs.AppendAsync("p", generation, "stdout", "abcdef", TestContext.Current.CancellationToken);
            var page = await new BehaviorLogStore(root, 10).ReadAsync("p", 0, 100, TestContext.Current.CancellationToken);
            Assert.True(page.Truncated);
            Assert.Equal("abcdef", Assert.Single(page.Entries).Message);
            Assert.Equal(2, page.LastSequence);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}