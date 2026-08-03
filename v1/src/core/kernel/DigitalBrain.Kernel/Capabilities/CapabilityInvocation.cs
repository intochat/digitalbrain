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

    internal static bool IsEnumerationDispatch(MethodInfo? dispatchedMethod)
        => dispatchedMethod?.DeclaringType == typeof(IAsyncEnumerableGrainExtension);

    internal static bool IsEnumerationStart(MethodInfo? dispatchedMethod)
        => IsEnumerationDispatch(dispatchedMethod, nameof(IAsyncEnumerableGrainExtension.StartEnumeration));

    internal static bool IsEnumerationContinuation(MethodInfo? dispatchedMethod)
        => IsEnumerationDispatch(dispatchedMethod, nameof(IAsyncEnumerableGrainExtension.MoveNext));

    internal static bool IsEnumerationDisposal(MethodInfo? dispatchedMethod)
        => IsEnumerationDispatch(dispatchedMethod, nameof(IAsyncEnumerableGrainExtension.DisposeAsync));

    internal static Guid? EnumerationId(IInvokable? dispatchedRequest)
        => dispatchedRequest?.GetArgumentCount() > 0 && dispatchedRequest.GetArgument(0) is Guid enumerationId
            ? enumerationId
            : null;

    internal static CapabilityOutcome? EnumerationTerminus(object? dispatchedResult)
    {
        if (dispatchedResult is not ValueTuple<EnumerationResult, object>(var status, _))
        {
            return null;
        }

        if ((status & EnumerationResult.Completed) != 0)
        {
            return CapabilityOutcome.Completed;
        }

        return (status & AbortedEnumeration) != 0 ? CapabilityOutcome.Failed : null;
    }

    private static bool IsEnumerationDispatch(MethodInfo? dispatchedMethod, string dispatchedMethodName)
        => IsEnumerationDispatch(dispatchedMethod)
            && string.Equals(dispatchedMethod!.Name, dispatchedMethodName, StringComparison.Ordinal);

    private const EnumerationResult AbortedEnumeration =
        EnumerationResult.MissingEnumeratorError | EnumerationResult.Error | EnumerationResult.Canceled;
}
