using System.Reflection;
using Orleans;

namespace DigitalBrain.Poc.Runtime;

internal static class ContractAlias
{
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var attribute = type.GetCustomAttributesData()
            .SingleOrDefault(candidate => candidate.AttributeType == typeof(AliasAttribute));
        if (attribute is null ||
            attribute.ConstructorArguments.Count != 1 ||
            attribute.ConstructorArguments[0].Value is not string alias ||
            string.IsNullOrWhiteSpace(alias))
        {
            return type.FullName ?? type.Name;
        }

        return alias;
    }
}
