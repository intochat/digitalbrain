using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CompositionFacts
{
    [Fact]
    public async Task HostInventoryListsTheSelectedModulesInDependencyOrder()
    {
        await using var brain = await UnitTest.Create().WithModule<Dependency>().WithModule<Dependent, DependentOptions>()
            .StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal([typeof(Dependency), typeof(Dependent)],
            brain.SiloServices.GetRequiredService<ModuleInventory>().Types);
    }

    [Fact]
    public void DefinitionCopiesConfigurationAndDeduplicatesIdenticalModules()
    {
        var values = new Dictionary<string, string?> { ["Feature:Enabled"] = "false" };
        var first = new ModuleDefinition(typeof(ExampleModule), values);
        values["Feature:Enabled"] = "true";
        Assert.Equal("false", first.Configuration["Feature:Enabled"]);
        Assert.Single(ModuleComposition.Resolve([first, new(typeof(ExampleModule), new Dictionary<string, string?> { ["Feature:Enabled"] = "false" })]));
    }

    [Fact]
    public void ConflictingDefinitionsFail()
    {
        var first = new ModuleDefinition(typeof(ExampleModule), new Dictionary<string, string?> { ["Value"] = "1" });
        var second = new ModuleDefinition(typeof(ExampleModule), new Dictionary<string, string?> { ["Value"] = "2" });
        Assert.Throws<InvalidOperationException>(() => ModuleComposition.Resolve([first, second]));
    }

    [Fact]
    public void NonModuleTypeFailsImmediately()
        => Assert.Throws<ArgumentException>(() => new ModuleDefinition(typeof(string)));

    [Fact]
    public void DistinctModulesCannotDisagreeOnSharedSettings()
    {
        var first = new ModuleDefinition(typeof(Dependency), new Dictionary<string, string?> { ["Shared:Value"] = "first" });
        var second = new ModuleDefinition(typeof(Dependent), new Dictionary<string, string?> { ["Shared:Value"] = "second" });
        Assert.Throws<InvalidOperationException>(() => ModuleComposition.Resolve([first, second]));
    }

    [Fact]
    public async Task TransitiveDependencyRegistersExactlyOnce()
    {
        await using var brain = await UnitTest.Create().WithModule<Dependency>().WithModule<Dependent>()
            .ConfigureSilo(silo => Assert.Single(silo.Services, s => s.ServiceType == typeof(Marker)))
            .StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void BuildsAnImmutableSnapshotAndFreezesAllCapturedDrafts()
    {
        ExampleOptions? captured = null;
        ModuleConfiguration<ExampleModule>? module = null;
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule, ExampleOptions>(
            o => { captured = o; o.Endpoint = "chosen"; }, m => module = m);
        var snapshot = draft.Build();
        captured!.Endpoint = "mutated";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Assert.Single(snapshot.Modules).Configuration).Build();
        Assert.Equal("chosen", configuration.GetModuleOptions<ExampleOptions>(nameof(ExampleModule)).Endpoint);
        Assert.Same(snapshot, draft.Build());
        Assert.Throws<InvalidOperationException>(() => draft.ConfigureModule<ExampleModule, ExampleOptions>(_ => { }));
        Assert.Throws<InvalidOperationException>(() => module!.ConfigureLocalServices(_ => { }));
    }

    [Fact]
    public void DuplicateDeclarationAndMissingModuleFail()
    {
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule, ExampleOptions>();
        Assert.Throws<InvalidOperationException>(() => draft.WithModule<ExampleModule, ExampleOptions>());
        Assert.Throws<InvalidOperationException>(() => draft.ConfigureModule<OtherModule>(_ => { }));
        Assert.Throws<InvalidOperationException>(() => draft.ConfigureModule<Dependent, DependentOptions>(_ => { }));
    }

    [Fact]
    public void ConfigureModuleEditsTheDeclaredOptionsAndKeepsTheRest()
    {
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule, ExampleOptions>(o => { o.Endpoint = "first"; o.Delay = 5; });
        draft.ConfigureModule<ExampleModule, ExampleOptions>(o => o.Endpoint = null);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Assert.Single(draft.Build().Modules).Configuration).Build();
        var options = configuration.GetModuleOptions<ExampleOptions>(nameof(ExampleModule));
        Assert.Null(options.Endpoint);
        Assert.Equal(5, options.Delay);
        Assert.Equal("keep", options.Name);
    }

    [Fact]
    public void OverridesEditTheApplicationsOwnOptionsIncludingResetsToDefaults()
    {
        var application = new BrainCompositionBuilder().WithModule<ExampleModule, ExampleOptions>(
            o => { o.Endpoint = "old"; o.Enabled = true; o.Delay = 250; });
        var token = new CompositionOverrides()
            .ConfigureModule<ExampleModule, ExampleOptions>(o => { o.Endpoint = null; o.Enabled = false; }).Serialize();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            Assert.Single(application.ApplyOverrides(token).Build().Modules).Configuration).Build();
        var applied = configuration.GetModuleOptions<ExampleOptions>(nameof(ExampleModule));
        Assert.Null(applied.Endpoint);
        Assert.False(applied.Enabled);
        Assert.Equal(250, applied.Delay);
    }

    [Fact]
    public void OverridesForUndeclaredModulesAndUnknownTokensFail()
    {
        var token = new CompositionOverrides().ConfigureModule<ExampleModule, ExampleOptions>(_ => { }).Serialize();
        Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder().WithModule<OtherModule>().ApplyOverrides(token));
        Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder().ApplyOverrides("not-a-token"));
    }

    [Fact]
    public void SettingsFreeModulesNeedNoOptions()
        => Assert.Equal(typeof(OtherModule), Assert.Single(new BrainCompositionBuilder().WithModule<OtherModule>().Build().Modules).ModuleType);

    [Fact]
    public void OptionsValidationRunsAtBuild()
    {
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule, ExampleOptions>(o => o.Delay = -1);
        Assert.Throws<ArgumentOutOfRangeException>(draft.Build);
    }

    [Theory]
    [InlineData("Orleans:ClusterId")]
    [InlineData("ConnectionStrings:storage")]
    [InlineData("DigitalBrain:Testing:Overrides")]
    [InlineData("DigitalBrain:Modules:0")]
    [InlineData("DigitalBrain:AI:OpenAI:ApiKey")]
    [InlineData("Provider:PrivateKeyPem")]
    public void HarnessAndCredentialKeysCannotBePublicSettings(string key)
        => Assert.Throws<ArgumentException>(() => ModuleSettingsValidation.ValidatePublicSettings(
            [new(typeof(ExampleModule), new Dictionary<string, string?> { [key] = "override" })]));

    [Fact]
    public void LocalSubstitutionsCannotCrossProcess()
    {
        var overrides = new CompositionOverrides().ConfigureModule<OtherModule>(m => m.ConfigureLocalServices(_ => { }));
        Assert.Throws<NotSupportedException>(overrides.Serialize);
    }

    public sealed class Marker;
    public sealed class OtherModule : IModule { public void Configure(ISiloBuilder silo) { } }
    public sealed class ExampleModule : IModule<ExampleOptions> { public void Configure(ISiloBuilder silo) { } }
    public sealed class ExampleOptions : IModuleOptions
    {
        public string? Endpoint { get; set; }
        public bool Enabled { get; set; }
        public int Delay { get; set; }
        public string Name { get; set; } = "keep";
        public void Validate() => ArgumentOutOfRangeException.ThrowIfNegative(Delay);
    }
    public sealed class Dependency : IModule
    {
        public void Configure(ISiloBuilder silo) => silo.Services.AddSingleton<Marker>();
    }
    public sealed class Dependent : IModule<DependentOptions>
    {
        public void Configure(ISiloBuilder silo)
        {
            if (!silo.Services.Any(s => s.ServiceType == typeof(Marker)))
            { throw new InvalidOperationException("Dependency was not configured before dependent."); }
        }
    }
    public sealed class DependentOptions : IModuleOptions { public void Validate() { } }
}
