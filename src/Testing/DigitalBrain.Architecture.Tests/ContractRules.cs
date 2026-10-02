using System.Reflection;
using DigitalBrain.Core;

namespace DigitalBrain.Architecture.Tests;

internal static class ContractRules
{
    internal static IEnumerable<string> CredentialMembers(Type type) => CredentialMembers(type, type.FullName!, []);

    private static IEnumerable<string> CredentialMembers(Type type, string path, HashSet<Type> ancestors)
    {
        if (type.IsArray)
        {
            foreach (var violation in CredentialMembers(type.GetElementType()!, path + "[]", ancestors)) { yield return violation; }
            yield break;
        }
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var violation in CredentialMembers(argument, path + "[]", ancestors)) { yield return violation; }
            }
        }
        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true || !ancestors.Add(type)) { yield break; }
        foreach (var member in PublicMembers(type))
        {
            var memberPath = path + "." + member.Name;
            if (ModuleSettingsValidation.IsCredentialName(member.Name)) { yield return memberPath; }
            foreach (var violation in CredentialMembers(MemberType(member), memberPath, ancestors)) { yield return violation; }
        }
        ancestors.Remove(type);
    }

    internal static IEnumerable<string> TestHooks(Type type) =>
        type.GetInterfaces().Append(type).SelectMany(contract => contract.GetMethods()).Distinct()
            .Where(method => IsTestHookName(method.Name)
                || method.GetCustomAttributesData().Any(attribute => IsTestingType(attribute.AttributeType))
                || method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType).Any(IsTestingType))
            .Select(method => $"{type.FullName}.{method.Name}");

    private static bool IsTestHookName(string name)
    {
        if (name.EndsWith("Async", StringComparison.Ordinal)) { name = name[..^5]; }
        return name.EndsWith("ForTesting", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ForTest", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ForTests", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("TestOnly", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestingType(Type type) =>
        type.Assembly.GetName().Name is { } name
        && (name.Contains(".Testing", StringComparison.Ordinal) || name.Contains(".Tests", StringComparison.Ordinal)
            || name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.Ordinal)
            || (type.HasElementType && IsTestingType(type.GetElementType()!))
            || (type.IsGenericType && type.GetGenericArguments().Any(IsTestingType)));

    internal static IEnumerable<MemberInfo> PublicMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0)
            .Cast<MemberInfo>().Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance));

    internal static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new ArgumentException("Expected a property or field.", nameof(member))
    };
}
