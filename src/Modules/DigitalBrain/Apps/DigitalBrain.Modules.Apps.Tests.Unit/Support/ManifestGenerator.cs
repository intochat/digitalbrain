using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Types;
using Orleans.Concurrency;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// A manifest is generated from the neuron interface, never hand-written, so an operation list cannot
// drift from the code it describes. [Alias] names the app, [ReadOnly] marks an operation as a read,
// [Description] becomes the model-facing description.
internal static class ManifestGenerator
{
    public static AppManifest FromInterface<TNeuron>(AppManifestSeed seed) where TNeuron : INeuron
        => FromInterface(typeof(TNeuron), seed);

    public static AppManifest FromInterface(Type neuronInterface, AppManifestSeed seed)
    {
        ArgumentNullException.ThrowIfNull(neuronInterface);
        ArgumentNullException.ThrowIfNull(seed);
        if (!typeof(INeuron).IsAssignableFrom(neuronInterface))
        {
            throw new ArgumentException($"{neuronInterface.Name} is not a neuron interface.", nameof(neuronInterface));
        }

        var alias = neuronInterface.GetCustomAttribute<AliasAttribute>()?.Alias;
        var id = seed.Id ?? alias
            ?? throw new ArgumentException($"{neuronInterface.Name} declares no [Alias] and the seed names no app id.");

        var manifest = new AppManifest
        {
            Id = id,
            Version = seed.Version,
            Publisher = seed.Publisher,
            Kind = seed.Kind,
            Name = seed.Name ?? TrimInterfacePrefix(neuronInterface.Name),
            DescriptionForPeople = seed.DescriptionForPeople,
            DescriptionForModel = seed.DescriptionForModel,
            Operations = Operations(neuronInterface),
            UiEntry = seed.UiEntry,
            Permissions = seed.Permissions,
            Meters = seed.Meters,
            ExamplePrompts = seed.ExamplePrompts,
            Scenarios = seed.Scenarios,
        };
        ManifestValidator.Validate(manifest);
        return manifest;
    }

    public static IReadOnlyList<AppOperation> Operations(Type neuronInterface)
    {
        var operations = new List<AppOperation>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in neuronInterface.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsAppOperation)
            .OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            var operation = ToOperation(method);
            if (!used.Add(operation.Name))
            {
                for (var suffix = 2; ; suffix++)
                {
                    var candidate = operation.Name + "-" + suffix;
                    if (used.Add(candidate)) { operation = operation with { Name = candidate }; break; }
                }
            }

            operations.Add(operation);
        }

        return operations;
    }

    private static AppOperation ToOperation(MethodInfo method) => new()
    {
        Name = method.Name,
        DescriptionForModel = method.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? $"{method.Name} on {method.DeclaringType!.Name}.",
        ReadOnly = method.GetCustomAttribute<Orleans.Concurrency.ReadOnlyAttribute>() is not null,
        InputTypeIds = method.GetParameters().ToDictionary(
            parameter => parameter.Name ?? "value",
            parameter => TypeId(parameter.ParameterType),
            StringComparer.Ordinal),
        OutputTypeId = OutputTypeId(method.ReturnType),
    };

    private static bool IsAppOperation(MethodInfo method)
    {
        if (method.IsSpecialName) { return false; }
        var declaring = method.DeclaringType;
        return declaring is not null
            && declaring != typeof(INeuron)
            && declaring != typeof(IGrainWithStringKey)
            && declaring != typeof(IAddressable)
            && declaring != typeof(object);
    }

    private static string? OutputTypeId(Type returnType)
    {
        var result = Unwrap(returnType);
        return result is null || result == typeof(void) ? null : TypeId(result);
    }

    private static Type? Unwrap(Type returnType)
    {
        if (!returnType.IsGenericType) { return null; }
        var definition = returnType.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>)
            ? returnType.GetGenericArguments()[0]
            : null;
    }

    private static string TypeId(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string) || underlying == typeof(Guid) || underlying == typeof(byte[])) { return "plain-text"; }
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short)
            || underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float)) { return "number"; }
        if (underlying == typeof(bool)) { return "boolean"; }
        if (underlying == typeof(DateOnly)) { return "date"; }
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset)) { return "date-time"; }
        if (underlying == typeof(SecretRef)) { return "secret"; }
        return "reference";
    }

    private static string TrimInterfacePrefix(string name) =>
        name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name[1..] : name;
}
