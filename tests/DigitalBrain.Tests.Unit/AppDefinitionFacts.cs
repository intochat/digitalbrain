#pragma warning disable CA1822, CA1068, DB0014
using DigitalBrain.Capabilities;

namespace DigitalBrain.Tests.Unit;

public sealed class AppDefinitionFacts
{
    [Fact]
    public void Discovery_resolves_alias_and_parameters_without_constructing_app()
    {
        var definition = new AppDefinition(typeof(TemplateApp));
        Assert.Equal("definition-template", definition.Manifest.Id);
        Assert.Equal("TemplateApp", definition.Manifest.Name);
        Assert.Equal([new CapabilityId("definition-capability", 1)], definition.Manifest.Requires.Capabilities);
        Assert.Equal(typeof(TemplateApp), definition.AppType);
        var method = definition.FindOperation("o-P-e-N");
        Assert.NotNull(method);
        Assert.Equal("Open", method.Name);
        Assert.Equal([typeof(string), typeof(CancellationToken)], method.GetParameters().Select(p => p.ParameterType));
        Assert.Same(method, definition.Operations["open"]);
        Assert.Null(definition.FindOperation("missing"));
    }

    [Fact]
    public void Declared_manifest_is_used_and_legacy_return_shapes_are_discovered()
    {
        var definition = new AppDefinition(typeof(DeclaredApp));
        Assert.Same(DeclaredApp.Manifest, definition.Manifest);
        Assert.Equal(4, definition.Operations.Count);
        Assert.NotNull(definition.FindOperation("Sync"));
        Assert.NotNull(definition.FindOperation("WithResult"));
        Assert.NotNull(definition.FindOperation("Value"));
        Assert.NotNull(definition.FindOperation("TokenOnly"));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(App))]
    [InlineData(typeof(UnmarkedApp))]
    [InlineData(typeof(GenericApp<>))]
    [InlineData(typeof(MismatchedApp))]
    [InlineData(typeof(DuplicateApp))]
    [InlineData(typeof(StaticHandlerApp))]
    [InlineData(typeof(GenericHandlerApp))]
    [InlineData(typeof(TwoInputApp))]
    [InlineData(typeof(TokenFirstApp))]
    [InlineData(typeof(TwoTokensApp))]
    [InlineData(typeof(ByRefApp))]
    [InlineData(typeof(ReturnValueApp))]
    public void Invalid_definitions_fail_during_discovery(Type type)
        => Assert.Throws<InvalidOperationException>(() => new AppDefinition(type));

    [Fact]
    public void Duplicate_alias_error_identifies_the_app_and_operation()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new AppDefinition(typeof(DuplicateApp)));
        Assert.Contains(nameof(DuplicateApp), error.Message, StringComparison.Ordinal);
        Assert.Contains("Open", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Capability("definition-capability", 1)]
    public interface IDefinitionCapability;

    [App("definition-template")]
    private sealed class TemplateApp : App
    {
        public TemplateApp(IReadOnlyList<IDefinitionCapability> capabilities) => throw new InvalidOperationException("Must not construct during discovery.");
        [On] public Task Open(string input, CancellationToken ct) => Task.CompletedTask;
    }

    [App("definition-declared")]
    private sealed class DeclaredApp : App
    {
        public static AppManifest Manifest { get; } = new("definition-declared", "Declared", "2.3", [], new AppRequirements([]));
        [On] public void Sync() { }
        [On] public Task<int> WithResult() => Task.FromResult(1);
        [On] public ValueTask Value() => ValueTask.CompletedTask;
        [On] public Task TokenOnly(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class UnmarkedApp : App;
    [App("generic")] private sealed class GenericApp<T> : App;
    [App("mismatch")]
    private sealed class MismatchedApp : App
    {
        public static AppManifest Manifest { get; } = new("other", "Other", "1", [], new AppRequirements([]));
    }
    [App("duplicate")]
    private sealed class DuplicateApp : App
    {
        [On] public void Open() { }
        [On] public void OPEN(string input) { }
    }
    [App("static")] private sealed class StaticHandlerApp : App { [On] public static void Open() { } }
    [App("generic-handler")] private sealed class GenericHandlerApp : App { [On] public void Open<T>() { } }
    [App("two-input")] private sealed class TwoInputApp : App { [On] public void Open(string a, string b) { } }
    [App("token-first")] private sealed class TokenFirstApp : App { [On] public void Open(CancellationToken ct, string input) { } }
    [App("two-tokens")] private sealed class TwoTokensApp : App { [On] public void Open(CancellationToken a, CancellationToken b) { } }
    [App("byref")] private sealed class ByRefApp : App { [On] public void Open(ref string input) { } }
    [App("return")] private sealed class ReturnValueApp : App { [On] public int Open() => 1; }
}
