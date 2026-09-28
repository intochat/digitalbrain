using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using DigitalBrain.Contracts;

namespace DigitalBrain.Core.Registry;

// One callable method of a public neuron contract, addressed as "{contract alias}/{method}".
public sealed partial record NeuronMethod(NeuronContract Contract, MethodInfo Method)
{
    public string Id => Contract.Id + "/" + Method.Name;

    public string? Description => Method.GetCustomAttribute<DescriptionAttribute>()?.Description;

    public IEnumerable<ParameterInfo> Arguments => Method.GetParameters().Where(static parameter => parameter.ParameterType != typeof(CancellationToken));

    public string SearchText => string.Join(' ', new[] { Words(Contract.Name), Words(Method.Name) }
        .Concat(Arguments.SelectMany(static parameter => ParameterWords(parameter)))
        .Append(Description ?? ""));

    internal static IReadOnlyList<NeuronMethod> Of(NeuronContract contract) =>
        [.. new[] { contract.Interface }.Concat(contract.Interface.GetInterfaces())
            .Where(static type => !type.IsAssignableFrom(typeof(INeuron)))
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(Callable)
            .GroupBy(static method => method.Name, StringComparer.Ordinal)
            .Select(group => new NeuronMethod(contract, group.First()))];

    private static bool Callable(MethodInfo method) =>
        !method.IsGenericMethodDefinition && !method.IsSpecialName && Awaitable(method.ReturnType)
        && method.GetParameters().All(static parameter => !parameter.ParameterType.IsByRef
            && !typeof(IAddressable).IsAssignableFrom(parameter.ParameterType));

    private static bool Awaitable(Type type) => type == typeof(Task) || type == typeof(ValueTask)
        || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)));

    private static IEnumerable<string> ParameterWords(ParameterInfo parameter)
    {
        yield return Words(parameter.Name ?? "");
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        if (type.IsPrimitive || type == typeof(string) || type.IsArray || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) { yield break; }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) { yield return Words(property.Name); }
    }

    private static string Words(string name) => CamelBoundary().Replace(name, " ");

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelBoundary();
}
