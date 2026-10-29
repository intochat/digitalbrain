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
    /// The manifest is the app's public static <see cref="AppManifest"/> <c>Manifest</c> property when it has one. Otherwise it is read from the class:
    /// the id from <c>[App]</c>, the requirements from the <c>[Capability]</c> contracts its constructor takes, and the neurons from the
    /// <c>[Neuron]</c> classes in the app's namespace.
    /// </remarks>
    /// <exception cref="CapabilityNotBoundException">A required capability has no bound fake; nothing was registered or built.</exception>
    /// <exception cref="InvalidOperationException">The class has no <c>[App]</c> attribute, or it is already installed.</exception>
    public async Task<InstalledApp<TApp>> Install<TApp>() where TApp : App
    {
        var definition = new AppDefinition(typeof(TApp));
        var manifest = definition.Manifest;
        foreach (var capability in manifest.Requires.Capabilities)
        {
            if (!Capabilities.IsBound(capability))
            {
                throw new CapabilityNotBoundException(capability, null, $"App '{manifest.Id}'");
            }
        }
        if (!_apps.TryAdd(manifest.Id, typeof(TApp)))
        {
            throw new InvalidOperationException($"App '{manifest.Id}' is already installed in this TestOS.");
        }
        foreach (var neuron in manifest.Neurons) { Register(neuron); }
        var app = new InstalledApp<TApp>(this, definition);
        await app.Start().ConfigureAwait(false);
        return app;
    }

    /// <summary>The manifest <see cref="Install{TApp}"/> uses for <paramref name="appType"/>.</summary>
    /// <exception cref="InvalidOperationException">The class has no <c>[App]</c> attribute.</exception>
    public static AppManifest ManifestOf(Type appType) => new AppDefinition(appType).Manifest;
}
