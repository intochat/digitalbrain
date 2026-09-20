using DigitalBrain.Core;

namespace DigitalBrain.Tests;

public sealed class CompositionBuilderFacts
{
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
    {
        var result = new BrainCompositionBuilder().WithModule<OtherModule>().Build();
        Assert.Equal(typeof(OtherModule), Assert.Single(result.Modules).ModuleType);
    }

    [Fact]
    public void ModuleValidationRunsAtBuild()
    {
        var draft = new BrainCompositionBuilder().WithModule<ExampleModule>(m =>
            m.ConfigureOptions<ExampleOptions>(o => o.Delay = -1, "Delay"));
        Assert.Throws<ArgumentOutOfRangeException>(draft.Build);
    }

    [ModuleConfiguration(typeof(ExampleContract))]
    public sealed class ExampleModule : IModule { public void Configure(Orleans.Hosting.ISiloBuilder silo) { } }
    public sealed class OtherModule : IModule { public void Configure(Orleans.Hosting.ISiloBuilder silo) { } }
    public sealed class OtherOptions;
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
}
