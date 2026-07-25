using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

public sealed class DigitalBrainTestBuilder
{
    private readonly TestEdgeRegistry _edges = new();
    private readonly Dictionary<ModuleId, ICompiledModule> _modules = [];
    private bool _sealed;

    public void AddModule<TModule>()
        where TModule : class, IModule, new()
    {
        ThrowIfSealed();

        var compiled = (ICompiledModule)new TModule();
        if (!_modules.TryAdd(compiled.Id, compiled))
        {
            throw new InvalidOperationException(
                $"Module '{compiled.Id}' is already configured for this fixture.");
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void ConfigureChatClient<TService, TScript>(
        IReadOnlyCollection<Type> neuronAliases,
        TService adapter,
        TScript script,
        Action<TScript> reset)
        where TService : class
        where TScript : class
    {
        ThrowIfSealed();
        _edges.ConfigureChatClient(
            neuronAliases,
            adapter,
            script,
            reset);
    }

    internal TestFixtureComposition Seal()
    {
        _sealed = true;
        _edges.Seal();
        return new(
            _modules.Values.ToArray(),
            _edges);
    }

    private void ThrowIfSealed()
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                "The DigitalBrain test composition is already sealed.");
        }
    }
}

internal sealed record TestFixtureComposition(
    IReadOnlyCollection<ICompiledModule> Modules,
    TestEdgeRegistry Edges);
