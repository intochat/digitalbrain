using Brain.Client;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Brain.KernelTests;

public class BrainClusterCallerKeyTests
{
    [Fact]
    public void Forced_caller_key_wins_over_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BRAIN_CALLER"] = "owner|behavior/hash|behavior/hash" })
            .Build();

        var callerKey = BrainCluster.ResolveCallerKey("forced|actor/x|session/1", configuration);

        Assert.Equal("forced|actor/x|session/1", callerKey);
    }

    [Fact]
    public void Brain_caller_configuration_is_read_when_not_forced()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BRAIN_CALLER"] = "owner|behavior/hash|behavior/hash" })
            .Build();

        var callerKey = BrainCluster.ResolveCallerKey(null, configuration);

        Assert.Equal("owner|behavior/hash|behavior/hash", callerKey);
    }

    [Fact]
    public void Default_caller_key_is_used_when_neither_forced_nor_configured()
    {
        var configuration = new ConfigurationBuilder().Build();

        var callerKey = BrainCluster.ResolveCallerKey(null, configuration);

        Assert.StartsWith("local-owner|actor/script|session/", callerKey);
    }
}
