using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Instruct.tools names these contributions; brain operations are bound separately.
public sealed class NativeTools
{
    private readonly Dictionary<string, AIFunction> _functions = new(StringComparer.Ordinal);

    public bool Contains(string name) => _functions.ContainsKey(name);

    public void Add(string name, AIFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        _functions[name] = function;
    }

    public IEnumerable<AIFunction> Resolve(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            // Dropping a contributing module must not stop agents that once used it.
            if (_functions.TryGetValue(name, out var function))
            {
                yield return function;
            }
        }
    }
}
