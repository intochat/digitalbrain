using System.Reflection;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Core;

internal static class DescriptorRules
{
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
