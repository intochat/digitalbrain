using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Hosting;

public sealed class RuntimeOptionsFacts
{
    [Fact]
    public void DefaultNamesNoProduct()
    {
        foreach (var p in typeof(RuntimeOptions).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            if (p.GetValue(RuntimeOptions.Default) is string value)
            {
                Assert.DoesNotContain("IntoChat", value, StringComparison.OrdinalIgnoreCase);
            }
        }
        Assert.Null(RuntimeOptions.Default.OsImage);
        Assert.Null(RuntimeOptions.Default.SandboxImage);
    }

    [Fact]
    public void ResolveReturnsTheRegisteredInstance()
    {
        var custom = new RuntimeOptions { TopologyKey = "Custom:Topology", DefaultHomeDirectoryName = ".custom" };
        using var sp = new ServiceCollection().AddSingleton(custom).BuildServiceProvider();
        Assert.Same(custom, RuntimeOptions.Resolve(sp));
    }

    [Fact]
    public void ResolveWithoutRegistrationReturnsDefault()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();
        Assert.Same(RuntimeOptions.Default, RuntimeOptions.Resolve(sp));
    }

    [Fact]
    public void DefaultHomeIsDigitalBrainFolder()
    {
        Assert.EndsWith(".digitalbrain", StateHome.Resolve(null, RuntimeOptions.Default).Path);
    }

    [Fact]
    public void ConfiguredHomeKeyIsTheOneTheOptionsName()
    {
        var home = Path.Combine(Path.GetTempPath(), "rt-opts-home");
        var config = new ConfigurationBuilder().AddInMemoryCollection([new("DigitalBrain:Home", home), new("IntoChatOS:Home", "~/ignored")]).Build();
        Assert.Equal(Path.GetFullPath(home), StateHome.Resolve(config, RuntimeOptions.Default).Path);
    }
}
