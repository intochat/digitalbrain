using DigitalBrain.Capabilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration;

public sealed class LocalCloudProfileFacts
{
    private static readonly ModuleManifest Circle = new([new CapabilityId("circle", 1)], [], [], [], []) { Id = "circle" };

    private static IConfiguration Config(string? address) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [IntoChatOSNames.CloudServerAddressKey] = address }).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://server.example")]
    [InlineData("server.example")]
    public void A_missing_or_non_https_server_address_is_an_issue(string? address)
    {
        var issue = Assert.Single(new LocalCloudProfile().Validate(Config(address), [Circle]));
        Assert.Equal(IntoChatOSNames.CloudServerAddressKey, issue.Key);
    }

    [Fact]
    public void Without_a_module_providing_circle_1_there_is_an_issue_naming_it()
    {
        var other = new ModuleManifest([new CapabilityId("circle", 2)], [], [], [], []) { Id = "other" };
        var issue = Assert.Single(new LocalCloudProfile().Validate(Config("https://server.example"), [other]));
        Assert.Contains("circle@1", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_present_is_no_issue()
        => Assert.Empty(new LocalCloudProfile().Validate(Config("https://server.example"), [Circle]));

    [Fact]
    public void Bindings_and_services_equal_the_local_ones()
    {
        Assert.Equal(new LocalProfile().Bindings, new LocalCloudProfile().Bindings);
        Assert.Equal(new LocalProfile().Services, new LocalCloudProfile().Services);
        Assert.Equal(Topology.LocalCloud, new LocalCloudProfile().Topology);
    }

    [Fact]
    public void UseTopology_selects_local_cloud_and_reports_both_issues_in_one_throw()
    {
        using var home = new TempHome();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IntoChatOSNames.TopologyKey] = "local+cloud",
            [IntoChatOSNames.HomeKey] = home.Path,
        });
        builder.UseClusterStore(new FakeClusterStore());
        var error = Assert.Throws<InvalidOperationException>(() => builder.UseTopology([], []));
        Assert.Contains(IntoChatOSNames.CloudServerAddressKey, error.Message, StringComparison.Ordinal);
        Assert.Contains("circle@1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseTopology_accepts_a_valid_local_cloud_host()
    {
        using var home = new TempHome();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IntoChatOSNames.TopologyKey] = "local+cloud",
            [IntoChatOSNames.HomeKey] = home.Path,
            [IntoChatOSNames.CloudServerAddressKey] = "https://server.example",
        });
        builder.UseClusterStore(new FakeClusterStore());
        Assert.IsType<LocalCloudProfile>(builder.UseTopology([Circle], []));
    }
}
