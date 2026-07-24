using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

public sealed class DigitalBrainTestBuilder
{
    private readonly Dictionary<ModuleId, ICompiledModule> _modules = [];
    private bool _sealed;

    public void AddModule<TModule>()
        where TModule : class, IModule, new()
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                "The DigitalBrain test composition is already sealed.");
        }

        var compiled = (ICompiledModule)new TModule();
        if (!_modules.TryAdd(compiled.Id, compiled))
        {
            throw new InvalidOperationException(
                $"Module '{compiled.Id}' is already configured for this fixture.");
        }
    }

    internal IReadOnlyCollection<ICompiledModule> Seal()
    {
        _sealed = true;
        return _modules.Values.ToArray();
    }
}
