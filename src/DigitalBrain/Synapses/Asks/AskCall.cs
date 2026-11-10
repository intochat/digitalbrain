using System.Reflection;
using System.Text.Json;
using DigitalBrain.Invocation;

namespace DigitalBrain;

internal static class AskCall
{
    private static readonly NeuronBinder SharedBinder = new();

    /// <summary>Calls the one public method named <paramref name="operation"/> of <paramref name="contract"/> on <paramref name="capability"/>, its parameters taken by name from <paramref name="arguments"/>; returns the JSON result (JSON null for a method with no result).</summary>
    /// <exception cref="BindingException">No such method, more than one, an argument missing, extra or of the wrong shape, or the capability is not a <paramref name="contract"/>.</exception>
    internal static async Task<JsonElement> Invoke(Type contract, object capability, string operation, JsonElement arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(capability);

        if (!contract.IsInstanceOfType(capability))
        {
            throw new BindingException($"The capability is not a {contract.Name}.");
        }

        if (string.IsNullOrEmpty(operation))
        {
            throw new BindingException($"{contract.Name}: an ask needs an operation.");
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new BindingException($"{contract.Name}.{operation}: arguments must be a JSON object.");
        }

        var method = FindMethod(contract, operation);
        var ordered = OrderArguments(contract, method, arguments);
        // Collectible contract types are never cached statically so their load context can unload.
        var binder = contract.IsCollectible ? new NeuronBinder() : SharedBinder;
        var invocation = new RuntimeInvocation(string.Empty, contract.Name, string.Empty, operation, ordered);
        var bound = binder.Bind(contract, invocation);
        return await binder.InvokeAsync(capability, bound, ordered, ct).ConfigureAwait(true) ?? AskRules.NoResult;
    }

    private static MethodInfo FindMethod(Type contract, string operation)
    {
        IEnumerable<MethodInfo> methods = contract.IsInterface
            ? contract.GetMethods().Concat(contract.GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
            : contract.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        HashSet<MethodInfo> excluded = contract.IsInterface ? [] : contract.GetInterfaces()
            .Where(inherited => inherited == typeof(IDisposable) || inherited == typeof(IAsyncDisposable))
            .SelectMany(inherited => contract.GetInterfaceMap(inherited).TargetMethods)
            .ToHashSet();

        MethodInfo? found = null;
        foreach (var method in methods)
        {
            if (method.IsSpecialName || method.IsGenericMethodDefinition || method.IsStatic
                || method.DeclaringType == typeof(object) || method.DeclaringType == typeof(IDisposable) || method.DeclaringType == typeof(IAsyncDisposable)
                || excluded.Contains(method)
                || !string.Equals(method.Name, operation, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null && found != method)
            {
                throw new BindingException($"{contract.Name} has more than one method '{operation}'.");
            }

            found = method;
        }

        return found ?? throw new BindingException($"{contract.Name} has no method '{operation}'.");
    }

    private static JsonElement[] OrderArguments(Type contract, MethodInfo method, JsonElement arguments)
    {
        var parameters = method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToArray();
        var ordered = new JsonElement[parameters.Length];
        var seen = new bool[parameters.Length];

        foreach (var property in arguments.EnumerateObject())
        {
            var index = IndexOf(parameters, property.Name);
            if (index < 0)
            {
                throw new BindingException($"{contract.Name}.{method.Name}: unexpected argument '{property.Name}'.");
            }

            if (seen[index])
            {
                throw new BindingException($"{contract.Name}.{method.Name}: duplicate argument '{property.Name}'.");
            }

            seen[index] = true;
            ordered[index] = property.Value;
        }

        for (var i = 0; i < seen.Length; i++)
        {
            if (!seen[i])
            {
                throw new BindingException($"{contract.Name}.{method.Name}: missing argument '{parameters[i].Name}'.");
            }
        }

        return ordered;
    }

    private static int IndexOf(ParameterInfo[] parameters, string name)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
