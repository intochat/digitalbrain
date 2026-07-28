using System.Reflection;
using DigitalBrain.Abstractions;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Kernel;

internal static class CapabilityInvocation
{
    private static readonly HashSet<Type> FrameworkInterfaces =
    [
        typeof(INeuron),
        typeof(ISessionNeuron),
    ];

    internal static bool IsRequest(MethodInfo? method)
    {
        if (method?.DeclaringType is not { IsInterface: true } type)
        {
            return false;
        }

        if (FrameworkInterfaces.Contains(type))
        {
            return false;
        }

        return typeof(INeuron).IsAssignableFrom(type);
    }

    internal static bool IsRequest(MethodInfo? dispatchedMethod, IInvokable? dispatchedRequest)
        => ContractMethod(dispatchedMethod, dispatchedRequest) is not null;

    internal static MethodInfo? ContractMethod(MethodInfo? dispatchedMethod, IInvokable? dispatchedRequest)
    {
        if (IsRequest(dispatchedMethod))
        {
            return dispatchedMethod;
        }

        var enumerated = EnumeratedContractMethod(dispatchedMethod, dispatchedRequest);

        return IsRequest(enumerated) ? enumerated : null;
    }

    private static MethodInfo? EnumeratedContractMethod(MethodInfo? dispatchedMethod, IInvokable? dispatchedRequest)
    {
        if (dispatchedMethod?.DeclaringType != typeof(IAsyncEnumerableGrainExtension) || dispatchedRequest is null)
        {
            return null;
        }

        for (var index = 0; index < dispatchedRequest.GetArgumentCount(); index++)
        {
            if (dispatchedRequest.GetArgument(index) is IInvokable enumeration && IsEnumerationRequest(enumeration))
            {
                return enumeration.GetMethod();
            }
        }

        return null;
    }

    private static bool IsEnumerationRequest(IInvokable candidate)
        => candidate.GetType().GetInterfaces().Any(contract =>
            contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IAsyncEnumerableRequest<>));
}
