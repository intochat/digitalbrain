namespace DigitalBrain.Testing;

/// <summary>The brain-named entry point to <see cref="TestOS"/>: every static factory forwards to the same one on <see cref="TestOS"/>.</summary>
public static class TestBrain
{
    /// <summary>A local TestOS; see <see cref="TestOS.Local(Action{TestOSOptions}?)"/>.</summary>
    public static TestOS Local(Action<TestOSOptions>? configure = null) => TestOS.Local(configure);

    /// <summary>The definition <c>Install</c> uses for <paramref name="appType"/>; see <see cref="TestOS.DefinitionOf(Type)"/>.</summary>
    /// <exception cref="InvalidOperationException">The class has no <c>[App]</c> attribute.</exception>
    public static AppDefinition DefinitionOf(Type appType) => TestOS.DefinitionOf(appType);
}
