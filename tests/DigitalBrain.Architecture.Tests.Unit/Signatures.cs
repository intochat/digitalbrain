using System.Reflection;

namespace DigitalBrain.Architecture.Tests.Unit;

internal static class Signatures
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // Base type, interfaces, fields, method and constructor signatures; arrays, by-refs and generic arguments unwrapped.
    public static IEnumerable<Type> Of(Type type, bool visibleOnly)
    {
        IEnumerable<Type?> direct =
        [
            type.BaseType,
            .. type.GetInterfaces(),
            .. type.GetFields(Declared).Where(field => !visibleOnly || field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly).Select(field => field.FieldType),
            .. type.GetMethods(Declared).Where(method => !visibleOnly || Visible(method)).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)),
            .. type.GetConstructors(Declared).Where(constructor => !visibleOnly || Visible(constructor)).SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)),
        ];
        return direct.OfType<Type>().SelectMany(Unwrap).Where(mentioned => !mentioned.IsGenericParameter);
    }

    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static IEnumerable<Type> Unwrap(Type type)
        => type.HasElementType ? Unwrap(type.GetElementType()!)
            : type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Append(type.GetGenericTypeDefinition()) : [type];
}
