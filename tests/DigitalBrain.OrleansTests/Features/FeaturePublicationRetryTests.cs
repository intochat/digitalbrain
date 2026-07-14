extern alias McpProject;

using FeaturePublicationRetry = McpProject::DigitalBrain.Mcp.FeaturePublicationRetry;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeaturePublicationRetryTests
{
    [Fact]
    public async Task Transient_publication_failure_is_retried_to_completion()
    {
        var attempts = 0;

        await FeaturePublicationRetry.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 3
                ? Task.FromException(new IOException("transient"))
                : Task.CompletedTask;
        });

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Persistent_publication_failure_remains_visible_after_the_bound()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<IOException>(() => FeaturePublicationRetry.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.FromException(new IOException("persistent"));
        }));

        Assert.Equal(3, attempts);
    }
}
