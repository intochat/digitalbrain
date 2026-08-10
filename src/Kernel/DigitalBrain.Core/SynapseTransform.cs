using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using Orleans;

namespace DigitalBrain.Core;

public interface ISynapseTransform
{
    string Name { get; }

    Synapse Apply(Synapse synapse);
}

public sealed class DeclarativeSynapseTransform : ISynapseTransform
{
    private const string Prefix = "to:";

    private static readonly JsonSerializerOptions Shaping = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Type _targetType;
    private readonly IReadOnlyList<(string Target, string Source)> _mappings;

    private DeclarativeSynapseTransform(
        string name,
        Type targetType,
        IReadOnlyList<(string Target, string Source)> mappings)
    {
        Name = name;
        _targetType = targetType;
        _mappings = mappings;
    }

    public string Name { get; }

    public static DeclarativeSynapseTransform? TryParse(string transformName)
    {
        if (string.IsNullOrWhiteSpace(transformName)
            || !transformName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var spec = transformName[Prefix.Length..].Trim();
        var brace = spec.IndexOf('{', StringComparison.Ordinal);
        string alias;
        List<(string, string)> mappings = [];

        if (brace < 0)
        {
            alias = spec;
        }
        else
        {
            if (!spec.EndsWith('}'))
            {
                return null;
            }

            alias = spec[..brace].Trim();
            foreach (var pair in spec[(brace + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0 || separator == pair.Length - 1)
                {
                    return null;
                }

                mappings.Add((pair[..separator].Trim(), pair[(separator + 1)..].Trim()));
            }
        }

        return SynapseTypeIndex.FindByAlias(alias) is not { } targetType
            ? null
            : new DeclarativeSynapseTransform(transformName, targetType, mappings);
    }

    public Synapse Apply(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        if (JsonSerializer.SerializeToNode(synapse, synapse.GetType(), Shaping) is not JsonObject carried)
        {
            throw new InvalidOperationException(
                $"'{synapse.GetType().Name}' did not shape into a JSON object for '{Name}'.");
        }

        var shaped = new JsonObject();
        foreach (var (target, source) in _mappings)
        {
            if (carried.TryGetPropertyValue(JsonNamingPolicy.CamelCase.ConvertName(source), out var value))
            {
                shaped[JsonNamingPolicy.CamelCase.ConvertName(target)] = value?.DeepClone();
            }
        }

        return JsonSerializer.Deserialize(shaped, _targetType, Shaping) as Synapse
            ?? throw new InvalidOperationException(
                $"'{Name}' produced no '{_targetType.Name}' from a '{synapse.GetType().Name}'.");
    }
}

public static class SynapseTypeIndex
{
    private static readonly ConcurrentDictionary<string, Type?> Resolved = new(StringComparer.Ordinal);

    public static Type? FindByAlias(string synapseAlias)
    {
        if (string.IsNullOrWhiteSpace(synapseAlias))
        {
            return null;
        }

        return Resolved.GetOrAdd(synapseAlias, static alias =>
        {
            Type? found = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic
                    || assembly.GetName().Name is not { } name
                    || !name.StartsWith("DigitalBrain", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var type in SafeTypes(assembly))
                {
                    if (type is not { IsClass: true, IsAbstract: false }
                        || !typeof(Synapse).IsAssignableFrom(type)
                        || !string.Equals(SynapseAlias.Of(type), alias, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (found is not null && found != type)
                    {
                        return null;
                    }

                    found = type;
                }
            }

            return found;
        });
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }
}
