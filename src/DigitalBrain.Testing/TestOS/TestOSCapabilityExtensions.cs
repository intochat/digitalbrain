namespace DigitalBrain.Testing;

// The builder hooks for capability fakes. Capability-specific helpers (WithStorage, ...) arrive with their contracts as extension methods over With<TCapability>.
public sealed partial class TestOS
{
    /// <summary>Binds <paramref name="fake"/> as the <c>[Capability]</c> contract <typeparamref name="TCapability"/> named <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">The contract is not a <c>[Capability]</c> interface, or the (contract, name) pair is already bound.</exception>
    public TestOS With<TCapability>(string name, TCapability fake) where TCapability : class
    {
        Capabilities.Bind(name, fake);
        return this;
    }

    /// <summary>Binds <paramref name="fake"/> as <typeparamref name="TCapability"/> under the capability's own name.</summary>
    /// <exception cref="ArgumentException">The contract is not a <c>[Capability]</c> interface, or it is already bound under that name.</exception>
    public TestOS With<TCapability>(TCapability fake) where TCapability : class
        => With(CapabilityFakes.IdOf(typeof(TCapability)).Name, fake);
}
