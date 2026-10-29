using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

internal static class Config
{
    internal static IConfiguration Of(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();
}

public sealed class TopologySettingsFacts
{
    [Theory]
    [InlineData("local", Topology.Local)]
    [InlineData("LOCAL", Topology.Local)]
    [InlineData("local+cloud", Topology.LocalCloud)]
    [InlineData("Local+Cloud", Topology.LocalCloud)]
    [InlineData("cloud", Topology.Cloud)]
    public void Accepts_the_three_values(string value, Topology expected)
    {
        var pairs = new List<(string, string?)> { (IntoChatOSNames.TopologyKey, value) };
        if (expected == Topology.Cloud)
        {
            pairs.Add((IntoChatOSNames.CloudStorageKey, "s"));
            pairs.Add((IntoChatOSNames.CloudKeyVaultKey, "k"));
        }
        if (expected == Topology.LocalCloud) { pairs.Add((IntoChatOSNames.CloudServerAddressKey, "https://x")); }

        var settings = TopologySettings.Parse(Config.Of([.. pairs]), []);

        Assert.True(settings.IsValid, string.Join("; ", settings.Issues));
        Assert.Equal(expected, settings.Topology);
        Assert.Empty(settings.Warnings);
    }

    [Fact]
    public void Config_values_round_trip()
    {
        Assert.Equal("local", Topology.Local.ToConfigValue());
        Assert.Equal("local+cloud", Topology.LocalCloud.ToConfigValue());
        Assert.Equal("cloud", Topology.Cloud.ToConfigValue());
        foreach (var topology in Enum.GetValues<Topology>())
        {
            Assert.True(TopologyValidator.TryParse(topology.ToConfigValue(), out var parsed, out var alias));
            Assert.Equal(topology, parsed);
            Assert.False(alias);
        }
    }

    [Fact]
    public void Local_cloud_alias_is_accepted_with_a_warning()
    {
        var settings = TopologySettings.Parse(Config.Of((IntoChatOSNames.TopologyKey, "local-cloud"), (IntoChatOSNames.CloudServerAddressKey, "https://x")), []);

        Assert.True(settings.IsValid);
        Assert.Equal(Topology.LocalCloud, settings.Topology);
        Assert.Contains(settings.Warnings, w => w.Contains("local+cloud", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("hybrid")]
    [InlineData("loc-x")]
    [InlineData("clouds-y")]
    public void Unknown_value_names_the_three_allowed_values(string value)
    {
        var settings = TopologySettings.Parse(Config.Of((IntoChatOSNames.TopologyKey, value)), []);

        var issue = Assert.Single(settings.Issues);
        Assert.Equal(IntoChatOSNames.TopologyKey, issue.Key);
        Assert.Contains("local, local+cloud or cloud", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, issue.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Absent_topology_without_cloud_keys_is_local()
    {
        var settings = TopologySettings.Parse(Config.Of(), []);

        Assert.True(settings.IsValid);
        Assert.Equal(Topology.Local, settings.Topology);
        Assert.NotNull(settings.Home);
    }

    [Fact]
    public void Absent_topology_with_cloud_storage_asks_to_set_topology()
    {
        var settings = TopologySettings.Parse(Config.Of((IntoChatOSNames.CloudStorageKey, "x")), []);

        var issue = Assert.Single(settings.Issues);
        Assert.Contains("set IntoChatOS:Topology", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_home_is_an_issue_not_an_exception()
    {
        var settings = TopologySettings.Parse(Config.Of((IntoChatOSNames.HomeKey, "relative/path")), []);

        Assert.Null(settings.Home);
        Assert.Contains(settings.Issues, issue => issue.Key == IntoChatOSNames.HomeKey);
    }
}
