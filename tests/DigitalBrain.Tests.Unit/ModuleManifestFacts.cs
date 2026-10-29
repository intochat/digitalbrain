using DigitalBrain.Capabilities;

namespace DigitalBrain.Tests.Unit;

public sealed class SampleOptions
{
    public string? Value { get; set; }
}

[Module("sample.module")]
public sealed class SampleModule : Module
{
    public static ModuleManifest Manifest { get; } = new(
        Provides: [CapabilityId.Parse("storage@1")],
        Requires: [],
        Needs: [Need.State("drive"), Need.Http],
        Settings: [Setting.Of<SampleOptions>()],
        Secrets: [Secret.Named("google-oauth")]);
}

public sealed class UnmarkedModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [], [], []);
}

[Module("dup.provides")]
public sealed class DuplicateProvidesModule : Module
{
    public static ModuleManifest Manifest { get; } = new([Capability.Storage.V1, Capability.Storage.V1], [], [], [], []);
}

[Module("dup.requires")]
public sealed class DuplicateRequiresModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [Capability.Secrets.V1, CapabilityId.Parse("secrets@1")], [], [], []);
}

[Module("no.manifest")]
public sealed class ManifestlessModule : Module;

public sealed class ModuleManifestFacts
{
    [Fact]
    public void Of_reads_the_static_manifest_and_the_id()
    {
        var manifest = ModuleManifest.Of(typeof(SampleModule));
        Assert.Equal("sample.module", manifest.Id);
        Assert.Equal([new CapabilityId("storage", 1)], manifest.Provides);
        Assert.Equal([Need.State("drive"), Need.Http], manifest.Needs);
        Assert.Equal("google-oauth", Assert.Single(manifest.Secrets).Name);
    }

    [Fact]
    public void Of_without_module_attribute_names_the_type_and_attribute()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleManifest.Of(typeof(UnmarkedModule)));
        Assert.Contains(nameof(UnmarkedModule), error.Message, StringComparison.Ordinal);
        Assert.Contains("[Module", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Of_without_manifest_property_names_the_type_and_member()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleManifest.Of(typeof(ManifestlessModule)));
        Assert.Contains(nameof(ManifestlessModule), error.Message, StringComparison.Ordinal);
        Assert.Contains("Manifest", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Of_rejects_a_type_that_is_not_a_module()
    {
        Assert.Throws<InvalidOperationException>(() => ModuleManifest.Of(typeof(SampleOptions)));
    }

    [Fact]
    public void Setting_records_the_options_type_and_section()
    {
        var setting = Setting.Of<SampleOptions>();
        Assert.Equal(typeof(SampleOptions), setting.OptionsType);
        Assert.Equal("Sample", setting.Section);
        Assert.Equal("Custom", Setting.Of<SampleOptions>("Custom").Section);
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("../x")]
    [InlineData("")]
    public void Secret_rejects_unsafe_names(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => Secret.Named(name));
    }

    [Fact]
    public void Secret_keeps_its_name()
    {
        Assert.Equal("google-oauth", Secret.Named("google-oauth").Name);
    }

    [Fact]
    public void App_manifest_lists_required_capabilities()
    {
        var app = new AppManifest("notes", "Notes", "1.0.0", [typeof(SampleModule)], new AppRequirements([CapabilityId.Parse("storage@1")]));
        Assert.Equal("storage@1", Assert.Single(app.Requires.Capabilities).ToString());
    }

    [Theory]
    [InlineData(typeof(DuplicateProvidesModule), "dup.provides", "storage@1")]
    [InlineData(typeof(DuplicateRequiresModule), "dup.requires", "secrets@1")]
    public void Of_rejects_duplicate_capabilities_naming_module_and_id(Type module, string id, string capability)
    {
        var error = Assert.Throws<InvalidDataException>(() => ModuleManifest.Of(module));
        Assert.Contains(id, error.Message, StringComparison.Ordinal);
        Assert.Contains(capability, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void App_requirements_reject_duplicate_capabilities()
    {
        var error = Assert.Throws<InvalidDataException>(() => new AppRequirements([Capability.Storage.V1, Capability.Storage.V1]));
        Assert.Contains("storage@1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Core_has_no_type_named_after_retired_nouns()
    {
        var names = typeof(Signal).Assembly.GetTypes().Select(type => type.FullName!).ToList();
        Assert.DoesNotContain(names, name => name.Contains("Connector", StringComparison.OrdinalIgnoreCase));
    }
}
