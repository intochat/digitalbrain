using DigitalBrain.Coding;
using DigitalBrain.Gateway;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// The AppHost sets environment variables; the silo and the gateway read configuration keys. These facts
// pin that translation, so renaming a key on one side fails here instead of at a promotion.
public sealed class SlotConfigurationFacts
{
    // Exactly the names src/Aspire/DigitalBrain.AppHost/AppHost.cs sets on kernel-a, kernel-b and gateway.
    private static readonly (string Variable, string Value)[] AppHostEnvironment =
    [
        ("DigitalBrain__Slot", "b"),
        ("DigitalBrain__Slots__Gateway", "http://localhost:5080"),
        ("DigitalBrain__Slots__ArtifactsRoot", "E:/repo/artifacts"),
        ("DigitalBrain__Slots__a__Url", "http://localhost:5081"),
        ("DigitalBrain__Slots__b__Url", "http://localhost:5082"),
        ("DigitalBrain__Gateway__Slots__a", "http://localhost:5081"),
        ("DigitalBrain__Gateway__Slots__b", "http://localhost:5082"),
        ("DigitalBrain__Gateway__Active", "a"),
    ];

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(AppHostEnvironment.Select(entry =>
            new KeyValuePair<string, string?>(entry.Variable.Replace("__", ":", StringComparison.Ordinal), entry.Value)))
        .Build();

    [Fact]
    public void The_silo_reads_the_slot_the_app_host_gave_it()
    {
        var options = SlotOptions.From(Configuration());
        Assert.Equal("b", options.Slot);
        Assert.Equal(new Uri("http://localhost:5080"), options.GatewayUrl);
        Assert.Equal(new Uri("http://localhost:5081"), options.UrlFor("a"));
        Assert.Equal(new Uri("http://localhost:5082"), options.UrlFor("b"));
        Assert.Equal("kernel-a", options.ResourceFor("a"));
        Assert.Equal("kernel-b", options.ResourceFor("b"));
        // The AppHost names the output root, so the standby dll it starts and the tree the builder writes
        // are the same place whatever directory the silo happens to run in.
        Assert.Equal("E:/repo/artifacts/slot-b", options.ArtifactsFor("b", "E:/somewhere/else").Replace('\\', '/'));
    }

    [Fact]
    public void The_gateway_reads_both_slot_addresses_and_the_first_active_slot()
    {
        var options = GatewayOptions.From(Configuration());
        Assert.Equal(new Uri("http://localhost:5081"), options.Slots["a"]);
        Assert.Equal(new Uri("http://localhost:5082"), options.Slots["b"]);
        Assert.Equal("a", options.Active);
        Assert.Equal(TimeSpan.FromSeconds(15), options.MinSwitchInterval);
    }

    [Fact]
    public void The_slot_addresses_the_two_sides_agree_on_are_the_same()
    {
        var silo = SlotOptions.From(Configuration());
        var gateway = GatewayOptions.From(Configuration());
        foreach (var slot in SlotOptions.Names)
        {
            Assert.Equal(silo.UrlFor(slot), gateway.Slots[slot]);
        }
    }
}
