using System.Collections.Concurrent;

namespace DigitalBrain.Testing;

// Install<TApp>(): an [App] class with its neurons, its required capabilities checked against the bound fakes.
public sealed partial class TestOS
{
    private readonly ConcurrentDictionary<string, Type> _apps = new(StringComparer.Ordinal);

    /// <summary>
    /// Installs the <c>[App(id)]</c> class <typeparamref name="TApp"/>: checks every required capability against the bound fakes, registers the app's
    /// neurons, builds the app from <see cref="Services"/> and returns a handle to request its operations and read its presentation.
    /// </summary>
    /// <remarks>
    /// The definition is read from the class: the id from <c>[App]</c>, the requirements from the <c>[Capability]</c> contracts its constructor takes,
    /// and the neurons from the <c>[Neuron]</c> classes in the app's namespace.
    /// </remarks>
    /// <exception cref="CapabilityNotBoundException">A required capability has no bound fake; nothing was registered or built.</exception>
    /// <exception cref="InvalidOperationException">The class has no <c>[App]</c> attribute, or it is already installed.</exception>
    public async Task<InstalledApp<TApp>> Install<TApp>() where TApp : App
    {
        var definition = new AppDefinition(typeof(TApp));
        foreach (var capability in definition.Requires.Capabilities)
        {
            if (!Capabilities.IsBound(capability))
            {
                throw new CapabilityNotBoundException(capability, null, $"App '{definition.Id}'");
            }
        }
        if (!_apps.TryAdd(definition.Id, typeof(TApp)))
        {
            throw new InvalidOperationException($"App '{definition.Id}' is already installed in this TestOS.");
        }
        foreach (var neuron in definition.Neurons) { Register(neuron); }
        var app = new InstalledApp<TApp>(this, definition);
        await app.Start().ConfigureAwait(false);
        return app;
    }

    /// <summary>The definition <see cref="Install{TApp}"/> uses for <paramref name="appType"/>.</summary>
    /// <exception cref="InvalidOperationException">The class has no <c>[App]</c> attribute.</exception>
    public static AppDefinition DefinitionOf(Type appType) => new(appType);
}
