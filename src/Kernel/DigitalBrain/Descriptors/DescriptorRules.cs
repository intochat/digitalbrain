using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Core;

internal static class DescriptorRules
{
    internal static void ValidateMemberTypes(MethodInfo method, JsonTypeInfo typeInfo)
    {
        HashSet<Type> visited = [];
        var path = method.GetParameters().FirstOrDefault(parameter => parameter.ParameterType == typeInfo.Type)?.Name ?? "result";
        Visit(typeInfo, path);

        void VisitMember(Type type, string memberPath)
        {
            JsonTypeInfo contract;
            try
            {
                contract = typeInfo.Options.GetTypeInfo(type);
            }
            catch (Exception error) when (error is NotSupportedException or InvalidOperationException)
            {
                throw new InvalidOperationException($"Interface '{method.DeclaringType!.FullName}', method '{method.Name}', member '{memberPath}' needs JSON type '{type.FullName}'. Use a section 7 member type and add it to the assembly's source-generated JSON context.", error);
            }

            Visit(contract, memberPath);
        }

        void Visit(JsonTypeInfo contract, string memberPath)
        {
            var type = contract.Type;
            if (type == typeof(bool) || type == typeof(int) || type == typeof(long) || type == typeof(double)
                || type == typeof(decimal) || type == typeof(string) || type == typeof(NeuronId)
                || type == typeof(SignalId) || type == typeof(CorrelationId) || type == typeof(CommandId)
                || type == typeof(DateTimeOffset) || type == typeof(JsonElement))
            {
                return;
            }

            if (type.IsEnum && contract.GetJsonSchemaAsNode() is JsonObject schema
                && schema["enum"] is JsonArray values && values.All(value => value is JsonValue scalar && scalar.TryGetValue<string>(out _)))
            {
                return;
            }

            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                VisitMember(underlying, memberPath);
                return;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                VisitMember(type.GenericTypeArguments[0], memberPath + "[]");
                return;
            }

            if (!type.IsClass || type == typeof(object) || contract.Kind != JsonTypeInfoKind.Object
                || (type.Assembly != method.DeclaringType!.Assembly && type.Assembly != typeof(Command).Assembly))
            {
                throw new InvalidOperationException($"Interface '{method.DeclaringType!.FullName}', method '{method.Name}', member '{memberPath}' has unsupported type '{type.FullName}'. Use a section 7 member type, a string-serialized enum, IReadOnlyList<T>, or a DTO from the interface or kernel Contracts assembly.");
            }

            if (!visited.Add(type))
            {
                return;
            }

            foreach (var property in contract.Properties)
            {
                VisitMember(property.PropertyType, memberPath + "." + property.Name);
            }
        }
    }

    internal static void Validate(Type grainClass)
    {
        foreach (var contract in grainClass.GetInterfaces().Where(type => type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type)))
        {
            if (string.IsNullOrWhiteSpace(contract.GetCustomAttribute<AliasAttribute>()?.Alias))
            {
                throw new InvalidOperationException($"Interface '{contract.FullName}' must declare a non-empty [Alias]. Add an interface alias.");
            }

            HashSet<string> aliases = new(StringComparer.Ordinal);
            foreach (var method in contract.GetMethods())
            {
                var name = $"Interface '{contract.FullName}', method '{method.Name}'";
                var alias = method.GetCustomAttribute<AliasAttribute>()?.Alias;
                if (string.IsNullOrWhiteSpace(alias) || !aliases.Add(alias))
                {
                    throw new InvalidOperationException($"{name} must declare a non-empty [Alias] unique within the interface. Choose a unique method alias.");
                }

                var result = method.ReturnType;
                if (method.IsGenericMethodDefinition || (result != typeof(Task) && (!result.IsGenericType || result.GetGenericTypeDefinition() != typeof(Task<>))))
                {
                    throw new InvalidOperationException($"{name} must be a non-generic method returning Task or Task<T>. Change the method signature.");
                }

                var parameters = method.GetParameters();
                var hasCancellation = parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken);
                var arguments = hasCancellation ? parameters[..^1] : parameters;
                if ((hasCancellation && !parameters[^1].IsOptional) || arguments.Length > 1
                    || arguments.Any(parameter => !parameter.ParameterType.IsClass || parameter.ParameterType == typeof(string) || parameter.ParameterType.IsByRef))
                {
                    throw new InvalidOperationException($"{name} must take at most one reference type DTO and an optional trailing CancellationToken. Put arguments in a DTO.");
                }

                if (!method.IsDefined(typeof(ReadOnlyAttribute)) && (arguments.Length != 1 || !typeof(Command).IsAssignableFrom(arguments[0].ParameterType)))
                {
                    throw new InvalidOperationException($"{name} is a mutator and must take exactly one DTO deriving from Command. Derive its argument DTO from Command or mark a query [ReadOnly].");
                }
            }
        }
    }
}
