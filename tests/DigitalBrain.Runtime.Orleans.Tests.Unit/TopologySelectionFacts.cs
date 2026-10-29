using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class TopologySelectionFacts : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-sel-" + Guid.NewGuid().ToString("N"));

    private HostApplicationBuilder Builder(params (string Key, string? Value)[] pairs)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new(IntoChatOSNames.HomeKey, _home), .. pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))]);
        return builder;
    }

    [Fact]
    public void Cloud_without_cloud_implementation_names_the_azure_package()
    {
        var builder = Builder((IntoChatOSNames.TopologyKey, "cloud"), (IntoChatOSNames.CloudStorageKey, "s"), (IntoChatOSNames.CloudKeyVaultKey, "k"));

        var thrown = Assert.Throws<InvalidOperationException>(() => builder.UseTopology([], []));

        Assert.Contains("DigitalBrain.Runtime.Orleans.Azure", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_and_implementation_issues_are_thrown_once_together()
    {
        var builder = Builder((IntoChatOSNames.TopologyKey, "local"), ("IntoChatOS:Preset", "x"));
        var local = new FakeTopology(Topology.Local, issues: new TopologyIssue("IntoChatOS:Sample", "is wrong."));

        var thrown0 = Assert.Throws<InvalidOperationException>(() => builder.UseTopology([], [new("IntoChatOS:Preset", "IntoChatOS:Topology")], local));
        Assert.Contains("IntoChatOS:Preset", thrown0.Message, StringComparison.Ordinal);
        Assert.Contains("IntoChatOS:Sample: is wrong.", thrown0.Message, StringComparison.Ordinal);
        Assert.False(local.ServicesConfigured);

        var second = Builder((IntoChatOSNames.TopologyKey, "local"));
        var unbound = new FakeTopology(Topology.Local, Bindings.Unbound, issues: new TopologyIssue("IntoChatOS:Sample", "is wrong."));
        var thrown = Assert.Throws<InvalidOperationException>(() => second.UseTopology([Manifests.Needing("m", Need.Http)], [], unbound));
        Assert.Contains("IntoChatOS:Sample: is wrong.", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("module m", thrown.Message, StringComparison.Ordinal);
        Assert.False(unbound.ServicesConfigured);
    }

    [Fact]
    public void Settings_issue_and_implementation_issue_appear_in_one_exception()
    {
        // Home relative: a settings issue that does not block selection of the topology.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new(IntoChatOSNames.HomeKey, "relative/home")]);

        var thrown = Assert.Throws<InvalidOperationException>(() => builder.UseTopology([], [], new FakeTopology(Topology.Local)));

        Assert.Contains(IntoChatOSNames.HomeKey, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_selection_registers_and_configures()
    {
        var builder = Builder();
        var local = new FakeTopology(Topology.Local);

        var selected = builder.UseTopology([Manifests.Needing("m", Need.Http)], [], local, new FakeTopology(Topology.Cloud));

        Assert.Same(local, selected);
        Assert.True(local.ServicesConfigured);
        Assert.True(local.ConfigurationConfigured);
        using var host = builder.Build();
        Assert.Same(local, host.Services.GetRequiredService<ITopologyProfile>());
        Assert.Equal(Topology.Local, host.Services.GetRequiredService<TopologyContext>().Topology);
        Assert.Equal(TopologyContext.DefaultSiloPort, host.Services.GetRequiredService<TopologyContext>().SiloPort);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) { Directory.Delete(_home, recursive: true); }
    }
}
