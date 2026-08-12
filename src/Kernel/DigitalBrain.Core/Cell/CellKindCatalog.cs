using System.Collections.Concurrent;

namespace DigitalBrain.Core;

// Module kinds register here (Seam 3: CalculatorKind lives in Modules/Kinds).
// CellNeuron resolves by name without taking a project reference on modules.
public static class CellKindCatalog
{
    private static readonly ConcurrentDictionary<string, ICellKind> Kinds =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ICellKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind.Name);
        Kinds[kind.Name.Trim()] = kind;
    }

    internal static ICellKind Resolve(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (Kinds.TryGetValue(kind.Trim(), out var installed))
        {
            return installed;
        }

        var known = Kinds.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        var list = known.Length == 0 ? "(none registered)" : string.Join(", ", known);
        throw new NeuronAuthorizationException(
            $"Cell kind '{kind}' is not installed. Registered kinds: {list}. "
            + "Register via CellKindCatalog from a module IModule hook.");
    }
}
