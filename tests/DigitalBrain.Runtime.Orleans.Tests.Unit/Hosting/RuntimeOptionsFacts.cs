using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Hosting;

public sealed class RuntimeOptionsFacts
{
    [Fact]
    public void DefaultOptionsHaveNoProductName()
    {
        foreach (var p in typeof(RuntimeOptions).GetProperties().Where(p => p.PropertyType == typeof(string)))
            Assert.DoesNotContain("IntoChat", (string)p.GetValue(RuntimeOptions.Default)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ObsoleteNamesResolveTheSameHome()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([new("IntoChatOS:Home", "~/x")]).Build();
        #pragma warning disable CS0618
        Assert.Equal(StateHome.Resolve(config).Path, StateHome.Resolve(config, RuntimeOptions.IntoChatCompatible).Path);
        Assert.Equal(IntoChatOSNames.HomeKey, RuntimeOptions.IntoChatCompatible.HomeKey);
        #pragma warning restore CS0618
    }

    [Fact]
    public void DefaultHomeIsDigitalBrainFolder()
    {
        Assert.EndsWith(".digitalbrain", StateHome.Resolve(null, RuntimeOptions.Default).Path);
    }

    [Fact]
    public void ObsoleteShimKeepsEveryStringOfTheCompatibleOptions()
    {
        var o = RuntimeOptions.IntoChatCompatible;
        #pragma warning disable CS0618
        Assert.Equal(
            [IntoChatOSNames.TopologyKey, IntoChatOSNames.HomeKey, IntoChatOSNames.BindKey, IntoChatOSNames.CloudStorageKey,
             IntoChatOSNames.CloudKeyVaultKey, IntoChatOSNames.CloudServerAddressKey, IntoChatOSNames.MasterKeyKey,
             IntoChatOSNames.CloudSectionPrefix, IntoChatOSNames.Signalling.Section, IntoChatOSNames.Signalling.Retention,
             IntoChatOSNames.PubSubStore, IntoChatOSNames.SignalStreams, IntoChatOSNames.PlatformGrainStorage,
             IntoChatOSNames.ClusterGrainStorage, IntoChatOSNames.GrainStateContainer, StateHome.DefaultDirectoryName],
            [o.TopologyKey, o.HomeKey, o.BindKey, o.CloudStorageKey, o.CloudKeyVaultKey, o.CloudServerAddressKey, o.MasterKeyKey,
             o.CloudSectionPrefix, o.SignallingSection, o.RetentionSection, o.PubSubStore, o.SignalStreams, o.PlatformGrainStorage,
             o.ClusterGrainStorage, o.GrainStateContainer, o.DefaultHomeDirectoryName]);
        #pragma warning restore CS0618
    }

    [Fact]
    public void ConfiguredHomeKeyIsTheOneTheOptionsName()
    {
        var home = Path.Combine(Path.GetTempPath(), "rt-opts-home");
        var config = new ConfigurationBuilder().AddInMemoryCollection([new("DigitalBrain:Home", home), new("IntoChatOS:Home", "~/ignored")]).Build();
        Assert.Equal(Path.GetFullPath(home), StateHome.Resolve(config, RuntimeOptions.Default).Path);
    }
}
