using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// The tools an Instruct.tools entry can name by a plain word. Brain operations and typed
// neuron methods are bound separately.
public sealed class NativeTools
{
    private readonly Lazy<IReadOnlyDictionary<string, AIFunction>> _functions;

    public NativeTools(IEnumerable<INativeToolContributor> contributors, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(services);

        // Contributors run on first use rather than at registration, because one may depend on
        // a service the host registered after the module configured the silo. Several
        // activations reach this singleton at once, so Lazy makes that one build thread-safe.
        _functions = new(() =>
        {
            var functions = new Dictionary<string, AIFunction>(StringComparer.Ordinal);
            foreach (var contributor in contributors)
            {
                // Last registration wins, the way it does for every other service.
                functions[contributor.Name] = contributor.Create(services);
            }

            return functions;
        });
    }

    public bool Contains(string name) => _functions.Value.ContainsKey(name);

    public IEnumerable<AIFunction> Resolve(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            // Dropping a contributing module must not stop agents that once used it.
            if (_functions.Value.TryGetValue(name, out var function))
            {
                yield return function;
            }
        }
    }
}
