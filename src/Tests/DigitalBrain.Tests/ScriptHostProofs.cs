using DigitalBrain.Aspire;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ScriptHostProofs
{
    [Fact]
    public void StreamsFallBackToTheClusteringConnection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:clustering"] = "UseDevelopmentStorage=true",
            })
            .Build();

        var storage = DigitalBrainScriptHost.RequireStorage(configuration);

        Assert.Equal("UseDevelopmentStorage=true", storage.Clustering);
        Assert.Equal("UseDevelopmentStorage=true", storage.Streams);
    }

    [Fact]
    public void DistinctStreamsConnectionIsKept()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:clustering"] = "cluster-storage",
                ["ConnectionStrings:streams"] = "stream-storage",
            })
            .Build();

        var storage = DigitalBrainScriptHost.RequireStorage(configuration);

        Assert.Equal("cluster-storage", storage.Clustering);
        Assert.Equal("stream-storage", storage.Streams);
    }

    [Fact]
    public void MissingClusteringConnectionRefusesWithGuidance()
    {
        var configuration = new ConfigurationBuilder().Build();

        var refusal = Assert.Throws<InvalidOperationException>(
            () => DigitalBrainScriptHost.RequireStorage(configuration));

        Assert.Contains("ConnectionStrings:clustering", refusal.Message, StringComparison.Ordinal);
    }
}
