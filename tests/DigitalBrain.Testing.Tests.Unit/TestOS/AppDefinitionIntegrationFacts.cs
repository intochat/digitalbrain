#pragma warning disable CA1822, DB0014
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit;

public sealed class AppDefinitionIntegrationFacts
{
    [Fact]
    public async Task Shared_definition_preserves_dash_aliases_and_argument_conversion()
    {
        await using var os = OS.Local();
        var app = await os.Install<DefinitionIntegrationApp>();
        await app.Request("s-E-t-v-A-l-U-e", new { value = 7 });
        Assert.Equal(7, app.App.Value);
        await app.Request("SetValue");
        Assert.Equal(0, app.App.Value);
        Assert.Equal(["SetValue"], app.Operations);
        Assert.Same(DefinitionIntegrationApp.Manifest, app.Manifest);
        Assert.Same(DefinitionIntegrationApp.Manifest, OS.ManifestOf(typeof(DefinitionIntegrationApp)));
    }

    [Fact]
    public async Task Invalid_definition_fails_before_app_constructor_runs()
    {
        await using var os = OS.Local();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => os.Install<InvalidDefinitionApp>());
        Assert.Contains(nameof(InvalidDefinitionApp.Open), error.Message, StringComparison.Ordinal);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task Installation_reports_ambiguous_widest_constructors()
    {
        await using var os = OS.Local();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => os.Install<AmbiguousDefinitionApp>());
        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [App("definition-ambiguous")]
    public sealed class AmbiguousDefinitionApp : App
    {
        public AmbiguousDefinitionApp(string value) { }
        public AmbiguousDefinitionApp(int value) { }
    }
    [App("definition-integration")]
    public sealed class DefinitionIntegrationApp : App
    {
        public static AppManifest Manifest { get; } = new("definition-integration", "Definition", "1", [], new AppRequirements([]));
        public int Value { get; private set; }
        [On] public void SetValue(Input input) => Value = input.Value;
    }

    public sealed record Input(int Value);

    [App("definition-invalid")]
    public sealed class InvalidDefinitionApp : App
    {
        public InvalidDefinitionApp() => throw new InvalidOperationException("Constructor must not run.");
        [On] public void Open(string first, string second) { }
    }
}
