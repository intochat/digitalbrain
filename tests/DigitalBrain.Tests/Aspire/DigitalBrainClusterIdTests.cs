namespace DigitalBrain.Tests.Aspire;

public sealed class DigitalBrainClusterIdTests
{
    [Fact]
    public void ResolveLocalClusterId_Prefers_Explicit_DigitalBrain_Override()
    {
        var clusterId = global::DigitalBrain.Aspire.DigitalBrainBuilderExtensions.ResolveLocalClusterId(name =>
            name == "DIGITALBRAIN_CLUSTER_ID" ? "cluster-from-env" : null);

        Assert.Equal("cluster-from-env", clusterId);
    }

    [Fact]
    public void ResolveLocalClusterId_Falls_Back_To_Orleans_Override()
    {
        var clusterId = global::DigitalBrain.Aspire.DigitalBrainBuilderExtensions.ResolveLocalClusterId(name =>
            name == "Orleans__ClusterId" ? "cluster-from-orleans-env" : null);

        Assert.Equal("cluster-from-orleans-env", clusterId);
    }

    [Fact]
    public void ResolveLocalClusterId_Generates_Fresh_Local_Cluster_Id()
    {
        var first = global::DigitalBrain.Aspire.DigitalBrainBuilderExtensions.ResolveLocalClusterId(_ => null);
        var second = global::DigitalBrain.Aspire.DigitalBrainBuilderExtensions.ResolveLocalClusterId(_ => null);

        Assert.StartsWith("digitalbrain-dev-", first);
        Assert.StartsWith("digitalbrain-dev-", second);
        Assert.NotEqual(first, second);
    }
}
