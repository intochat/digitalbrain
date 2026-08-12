namespace DigitalBrain.Core;

public static class ContractSignature
{
    private const string AutoFilledCommandId = "CommandId";

    public static string Of(Type synapseType)
    {
        ArgumentNullException.ThrowIfNull(synapseType);

        var parameters = synapseType
            .GetConstructors()
            .OrderByDescending(static ctor => ctor.GetParameters().Length)
            .FirstOrDefault()?
            .GetParameters()
            .Where(static parameter => parameter.Name != null
                && !string.Equals(parameter.Name, AutoFilledCommandId, StringComparison.OrdinalIgnoreCase))
            .Select(static parameter =>
                $"{char.ToLowerInvariant(parameter.Name![0])}{parameter.Name[1..]}: {Named(parameter.ParameterType)}")
            ?? [];

        var reply = ReplyOf(synapseType);
        var rendered = $"{synapseType.Name}({string.Join(", ", parameters)})";
        return reply is null ? rendered : $"{rendered} → {reply.Name}";
    }

    private static Type? ReplyOf(Type synapseType)
    {
        for (var probed = synapseType.BaseType; probed is not null; probed = probed.BaseType)
        {
            if (probed.IsGenericType && probed.GetGenericTypeDefinition() == typeof(RequestSynapse<>))
            {
                return probed.GenericTypeArguments[0];
            }
        }

        return null;
    }

    private static string Named(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return $"{Named(inner)}?";
        }

        return type switch
        {
            _ when type == typeof(int) => "int",
            _ when type == typeof(long) => "long",
            _ when type == typeof(bool) => "bool",
            _ when type == typeof(double) => "double",
            _ when type == typeof(string) => "string",
            _ when type.IsArray => $"{Named(type.GetElementType()!)}[]",
            _ => type.Name,
        };
    }
}