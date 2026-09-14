using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Coding;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SlotServiceFacts
{
    private static SlotOptions Options(params (string Key, string Value)[] configured)
        => SlotOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(configured.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build());

    [Fact]
    public void Slot_options_default_to_the_gateway_and_the_two_kernel_ports()
    {
        var options = Options();
        Assert.Equal(["a", "b"], SlotOptions.Names);
        Assert.Equal("http://localhost:5080", options.GatewayUrl);
        Assert.Equal("http://localhost:5081", options.UrlFor("a"));
        Assert.Equal("http://localhost:5082", options.UrlFor("b"));
        Assert.Equal("kernel-a", options.ResourceFor("a"));
        Assert.Equal("kernel-b", options.ResourceFor("b"));
        Assert.Equal(TimeSpan.FromSeconds(10), options.Grace);
        Assert.Equal(TimeSpan.FromMinutes(20), options.PromoteWait);
        // Long enough for several of the interval the lease refresher polls on.
        Assert.Equal(TimeSpan.FromSeconds(10), options.LeaseSettle);
        Assert.True(options.LeaseSettle >= ActiveSlotNames.RefreshInterval * 2);
        Assert.Equal("/chats/slot-smoke/brain", options.SmokePath);
        Assert.Equal(120, options.HealthAttempts);
        Assert.Null(options.Slot);
    }

    [Fact]
    public void Configured_slots_win_over_the_defaults()
    {
        var options = Options(
            ("DigitalBrain:Slot", "b"),
            ("DigitalBrain:Slots:Gateway", "http://gateway:9000"),
            ("DigitalBrain:Slots:Grace", "00:00:02"),
            ("DigitalBrain:Slots:HealthAttempts", "7"),
            ("DigitalBrain:Slots:b:Url", "http://kernel-b:6000"),
            ("DigitalBrain:Slots:b:Resource", "silo-b"));
        Assert.Equal("b", options.Slot);
        Assert.Equal("http://gateway:9000", options.GatewayUrl);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Grace);
        Assert.Equal(7, options.HealthAttempts);
        Assert.Equal("http://kernel-b:6000", options.UrlFor("b"));
        Assert.Equal("silo-b", options.ResourceFor("b"));
        Assert.Equal("http://localhost:5081", options.UrlFor("a"));
    }

    [Fact]
    public void An_unknown_slot_is_named_in_the_refusal()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Options().UrlFor("c"));
        Assert.Contains("DigitalBrain:Slots:c:Url", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_artifacts_path_is_absolute_under_the_solution()
    {
        var artifacts = Options().ArtifactsFor("b", "E:/repo");
        Assert.Equal("E:/repo/artifacts/slot-b", artifacts.Replace('\\', '/'));
        var configured = Options(("DigitalBrain:Slots:ArtifactsRoot", "out")).ArtifactsFor("b", "E:/repo");
        Assert.Equal("E:/repo/out/slot-b", configured.Replace('\\', '/'));
    }
}
