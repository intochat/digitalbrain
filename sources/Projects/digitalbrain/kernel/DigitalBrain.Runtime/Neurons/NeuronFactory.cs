using System.Collections.Concurrent;

namespace DigitalBrain.Runtime.Neurons;

/// <summary>
/// Coordinates dynamic Orleans neuron activation and fast in-memory mock setups,
/// completely bypassing slow Roslyn dynamic compilation pipelines.
/// </summary>
public static class NeuronFactory
{
    private static readonly ConcurrentDictionary<string, Func<string, object>> _mockFactoryRegistry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a mock neuron factory for in-memory testing.
    /// </summary>
    public static void RegisterMock(string fqn, Func<string, object> factory)
    {
        _mockFactoryRegistry[fqn] = factory;
    }

    /// <summary>
    /// Unregisters a mock neuron factory.
    /// </summary>
    public static void UnregisterMock(string fqn)
    {
        _mockFactoryRegistry.TryRemove(fqn, out _);
    }

    /// <summary>
    /// Resolves the neuron grain from Orleans or an in-memory mock if registered.
    /// </summary>
    public static TNeuron GetNeuron<TNeuron>(IGrainFactory grainFactory, string fqn, string id)
        where TNeuron : IAddressable
    {
        if (_mockFactoryRegistry.TryGetValue(fqn, out var mockFactory))
        {
            var mock = mockFactory(id);
            if (mock is TNeuron typedMock)
            {
                return typedMock;
            }
        }

        var grainId = GrainId.Create(GrainType.Create(fqn), id);
        return grainFactory.GetGrain<TNeuron>(grainId);
    }

    /// <summary>
    /// Resolves the neuron grain as a generic addressable element.
    /// </summary>
    public static IAddressable GetNeuron(IGrainFactory grainFactory, string fqn, string id)
    {
        if (_mockFactoryRegistry.TryGetValue(fqn, out var mockFactory))
        {
            return (IAddressable)mockFactory(id);
        }

        var grainId = GrainId.Create(GrainType.Create(fqn), id);
        return grainFactory.GetGrain(grainId);
    }
}
