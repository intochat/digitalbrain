using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CompositionFacts
{
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
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule>(m =>
        {
            module = m;
            m.ConfigureOptions<ExampleOptions>(o => { captured = o; o.Endpoint = "chosen"; }, "Endpoint");
        });
        var snapshot = draft.Build();
        captured!.Endpoint = "mutated";
        Assert.Equal("chosen", Assert.Single(snapshot.Modules).Configuration["Example:Endpoint"]);
        Assert.Same(snapshot, draft.Build());
        Assert.Throws<InvalidOperationException>(() => draft.ConfigureModule<ExampleModule>(_ => { }));
        Assert.Throws<InvalidOperationException>(() => module!.ConfigureOptions<ExampleOptions>(_ => { }, "Endpoint"));
    }

    [Fact]
    public void DuplicateDeclarationAndMissingOverrideFail()
    {
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule>();
        Assert.Throws<InvalidOperationException>(() => draft.WithModule<ExampleModule>());
        Assert.Throws<InvalidOperationException>(() => draft.ConfigureModule<OtherModule>(_ => { }));
        Assert.Throws<ArgumentException>(() => draft.ConfigureModule<ExampleModule>(m =>
            m.ConfigureOptions<OtherOptions>(_ => { }, "Endpoint")));
    }

    [Fact]
    public void ExplicitDefaultsAndClearingSurviveAnOverride()
    {
        var contract = new ExampleContract();
        var patch = contract.WriteOverride(new ExampleOptions { Endpoint = null, Enabled = false, Delay = 0 },
            ["Endpoint", "Enabled", "Delay"]);
        var applied = (ExampleOptions)contract.ApplyOverride(
            new ExampleOptions { Endpoint = "old", Enabled = true, Delay = 250 }, patch);
        Assert.Null(applied.Endpoint);
        Assert.False(applied.Enabled);
        Assert.Equal(0, applied.Delay);
        Assert.Equal("keep", applied.Name);
    }

    [Fact]
    public void UnknownMembersAndWrongOptionTypesFail()
    {
        var contract = new ExampleContract();
        Assert.Throws<ArgumentException>(() => contract.ApplyOverride(new ExampleOptions(), "{\"Unknown\":1}"));
        Assert.Throws<ArgumentException>(() => contract.WriteOverride(new ExampleOptions(), ["Unknown"]));
        Assert.Throws<ArgumentException>(() => contract.Compile(new OtherOptions()));
    }

    [Fact]
    public void SettingsFreeModulesNeedNoContract()
        => Assert.Equal(typeof(OtherModule), Assert.Single(new BrainCompositionBuilder().WithModule<OtherModule>().Build().Modules).ModuleType);

    [Fact]
    public void ModuleValidationRunsAtBuild()
    {
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule>(m =>
            m.ConfigureOptions<ExampleOptions>(o => o.Delay = -1, "Delay"));
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
    public void LocalSubstitutionsAndOversizedPayloadsCannotCrossProcess()
    {
        var overrides = new CompositionOverrides().ConfigureModule<ExampleModule>(m => m.ConfigureLocalServices(_ => { }));
        Assert.Throws<NotSupportedException>(overrides.Serialize);
        Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder().ApplyOverrides(new string('x', 32769)));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Version\":99,\"Modules\":[]}")]
    public void InvalidEnvelopeIsRejectedWithoutEchoingPayload(string payload)
    {
        var error = Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder().WithModule<ExampleModule>().ApplyOverrides(payload));
        Assert.DoesNotContain(payload, error.Message);
    }

    public sealed class Marker;
    public sealed class OtherModule : IModule { public void Configure(ISiloBuilder silo) { } }
    public sealed class OtherOptions;
    [ModuleConfiguration(typeof(ExampleContract))]
    public sealed class ExampleModule : IModule { public void Configure(ISiloBuilder silo) { } }
    public sealed class ExampleOptions
    {
        public string? Endpoint { get; set; }
        public bool Enabled { get; set; }
        public int Delay { get; set; }
        public string Name { get; set; } = "keep";
    }
    public sealed class ExampleContract : ModuleConfigurationContract<ExampleModule, ExampleOptions>
    {
        public ExampleContract() : base("Endpoint", "Enabled", "Delay", "Name") { }
        protected override ModuleDefinition Compile(ExampleOptions options)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(options.Delay);
            return new(typeof(ExampleModule), new Dictionary<string, string?> { ["Example:Endpoint"] = options.Endpoint });
        }
    }
    public sealed class Dependency : IModule
    {
        public void Configure(ISiloBuilder silo) => silo.Services.AddSingleton<Marker>();
    }
    [ModuleConfiguration(typeof(DependentContract))]
    public sealed class Dependent : IModule
    {
        public void Configure(ISiloBuilder silo)
        {
            if (!silo.Services.Any(s => s.ServiceType == typeof(Marker)))
            { throw new InvalidOperationException("Dependency was not configured before dependent."); }
        }
    }
    public sealed class DependentOptions;
    public sealed class DependentContract() : ModuleConfigurationContract<Dependent, DependentOptions>()
    {
        protected override ModuleDefinition Compile(DependentOptions options)
            => new(typeof(Dependent), dependencies: [new(typeof(Dependency))]);
    }
}
