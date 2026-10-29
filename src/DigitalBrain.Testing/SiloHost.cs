using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

/// <summary>The entry point to an in-process brain host. The host ships in DigitalBrain.Runtime.Orleans.</summary>
public static class SiloHost
{
    /// <summary>The assembly-qualified name of the runtime's factory.</summary>
    public const string FactoryTypeName = "DigitalBrain.Runtime.Orleans.Testing.LocalSiloHostFactory, DigitalBrain.Runtime.Orleans";

    /// <summary>Starts a local in-process host.</summary>
    /// <exception cref="InvalidOperationException">The test project does not reference DigitalBrain.Runtime.Orleans.</exception>
    public static Task<IInProcessHost> Local(Action<InProcessHostOptions>? configure = null, CancellationToken ct = default)
    {
        var options = new InProcessHostOptions();
        configure?.Invoke(options);
        return Factory().Start(options, ct);
    }

    internal static IInProcessHostFactory Factory(string typeName = FactoryTypeName)
    {
        Type? type;
        try
        {
            type = Type.GetType(typeName, throwOnError: false);
        }
        catch (Exception error) when (error is FileLoadException or BadImageFormatException)
        {
            type = null;
        }
        if (type is null || !typeof(IInProcessHostFactory).IsAssignableFrom(type) || Activator.CreateInstance(type) is not IInProcessHostFactory factory)
        {
            throw new InvalidOperationException("SiloHost.Local requires a reference to DigitalBrain.Runtime.Orleans. Add the package to the test project.");
        }
        return factory;
    }
}
