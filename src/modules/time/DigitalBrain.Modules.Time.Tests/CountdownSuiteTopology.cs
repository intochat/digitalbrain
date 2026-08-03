using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed class CountdownSuiteTopology(BrainTestClusters clusters) : CountdownTest
{
    [Fact(DisplayName = "Every class in the Time suite runs on the one cluster its composition boots")]
    public async Task TheTimeSuiteRunsOnOneCluster()
    {
        _ = await BrainAsync();

        Assert.True(clusters.HasBootedCluster(typeof(CountdownTest)));
        Assert.Equal(1, clusters.BootedClusters);
    }
}
