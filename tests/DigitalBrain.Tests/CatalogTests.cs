using Brain.Contracts;
using DigitalBrain.Tests;
using Xunit;

namespace Brain.KernelTests;

public class CatalogTests(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : BrainTest<WorkspaceKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Catalog_lists_registered_kinds()
    {
        var catalog = Neuron("catalog", "main");
        var snapshot = await catalog.ReadAsync("");

        Assert.Contains("\"kind\":\"chat\"", snapshot.StateJson);
        Assert.Contains("\"kind\":\"window\"", snapshot.StateJson);
        Assert.Contains("\"kind\":\"feed\"", snapshot.StateJson);
        Assert.Contains("\"kind\":\"effect\"", snapshot.StateJson);
        Assert.Contains("\"kind\":\"catalog\"", snapshot.StateJson);
    }

    [Fact]
    public async Task Invoking_catalog_fails_closed_with_zero_events()
    {
        var catalog = Neuron("catalog", Guid.NewGuid().ToString("N"));
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            catalog.InvokeAsync(new("catalog.read.v1", "{}", "cmd-1", OwnerSession)));

        Assert.Equal(BrainErrors.UnknownContract, exception.Code);
        var events = await catalog.ReadEventsAsync(0, 10);
        Assert.Empty(events.Events);
    }
}
