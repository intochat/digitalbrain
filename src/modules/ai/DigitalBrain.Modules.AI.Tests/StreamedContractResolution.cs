using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Orleans.CodeGeneration;
using Orleans.Serialization.Invocation;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class StreamedContractResolution
{
    private static readonly MethodInfo StartEnumeration =
        EnumerationDispatch(nameof(IAsyncEnumerableGrainExtension.StartEnumeration), parameterCount: 3);

    private static readonly MethodInfo MoveNext =
        EnumerationDispatch(nameof(IAsyncEnumerableGrainExtension.MoveNext), parameterCount: 1);

    private static readonly MethodInfo RespondStreaming = typeof(ILLM).GetMethod(nameof(ILLM.RespondStreaming))!;

    private static readonly MethodInfo Respond = typeof(ILLM).GetMethod(nameof(ILLM.Respond))!;

    [Fact(DisplayName = "a streamed capability call resolves to the neuron contract method behind Orleans' enumeration dispatch")]
    public void StreamedCapabilityResolvesToItsContractMethod()
    {
        using var enumeration = new EnumerationRequestDouble(RespondStreaming);
        using var dispatched = new InvokableDouble(
            StartEnumeration, Guid.NewGuid(), enumeration, CancellationToken.None);

        Assert.False(CapabilityInvocation.IsRequest(StartEnumeration));
        Assert.Equal(RespondStreaming, CapabilityInvocation.ContractMethod(StartEnumeration, dispatched));
        Assert.True(CapabilityInvocation.IsRequest(StartEnumeration, dispatched));
    }

    [Fact(DisplayName = "the Task-returning capability method resolves to itself")]
    public void TaskCapabilityResolvesToItself()
    {
        using var dispatched = new InvokableDouble(Respond, Array.Empty<ChatMessage>());

        Assert.Equal(Respond, CapabilityInvocation.ContractMethod(Respond, dispatched));
        Assert.True(CapabilityInvocation.IsRequest(Respond, dispatched));
    }

    [Fact(DisplayName = "reminders and outbox drain are not capability requests")]
    public void FrameworkCallsAreNotCapabilityRequests()
    {
        MethodInfo[] frameworkMethods =
        [
            typeof(IRemindable).GetMethod(nameof(IRemindable.ReceiveReminder))!,
            typeof(IOutboxDrain).GetMethod(nameof(IOutboxDrain.Drain))!,
        ];

        foreach (var frameworkMethod in frameworkMethods)
        {
            using var dispatched = new InvokableDouble(frameworkMethod);

            Assert.Null(CapabilityInvocation.ContractMethod(frameworkMethod, dispatched));
            Assert.False(CapabilityInvocation.IsRequest(frameworkMethod, dispatched));
        }
    }

    [Fact(DisplayName = "an enumeration whose request is not a neuron contract method resolves to nothing")]
    public void EnumerationOfAForeignContractResolvesToNothing()
    {
        var foreignMethod = typeof(IRemindable).GetMethod(nameof(IRemindable.ReceiveReminder))!;
        using var enumeration = new EnumerationRequestDouble(foreignMethod);
        using var dispatched = new InvokableDouble(
            StartEnumeration, Guid.NewGuid(), enumeration, CancellationToken.None);

        Assert.Null(CapabilityInvocation.ContractMethod(StartEnumeration, dispatched));
        Assert.False(CapabilityInvocation.IsRequest(StartEnumeration, dispatched));
    }

    [Fact(DisplayName = "MoveNext carries no enumeration request, so it resolves to nothing")]
    public void MoveNextResolvesToNothing()
    {
        using var dispatched = new InvokableDouble(MoveNext, Guid.NewGuid());

        Assert.Null(CapabilityInvocation.ContractMethod(MoveNext, dispatched));
        Assert.False(CapabilityInvocation.IsRequest(MoveNext, dispatched));
    }

    private static MethodInfo EnumerationDispatch(string name, int parameterCount)
        => typeof(IAsyncEnumerableGrainExtension)
            .GetMethods()
            .Single(method => method.Name == name && method.GetParameters().Length == parameterCount);

    private class InvokableDouble(MethodInfo method, params object?[] arguments) : IInvokable
    {
        public MethodInfo GetMethod() => method;

        public int GetArgumentCount() => arguments.Length;

        public object GetArgument(int index) => arguments[index]!;

        public void SetArgument(int index, object value) => arguments[index] = value;

        public Type GetInterfaceType() => method.DeclaringType!;

        public string GetInterfaceName() => method.DeclaringType!.FullName!;

        public string GetMethodName() => method.Name;

        public string GetActivityName() => $"{method.DeclaringType!.Name}/{method.Name}";

        public object GetTarget() => throw new NotSupportedException();

        public void SetTarget(ITargetHolder holder) => throw new NotSupportedException();

        public ValueTask<Response> Invoke() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class EnumerationRequestDouble(MethodInfo method)
        : InvokableDouble(method), IAsyncEnumerableRequest<ChatResponseUpdate>
    {
        public int MaxBatchSize { get; set; }

        public InvokeMethodOptions Options => InvokeMethodOptions.None;

        public void AddInvokeMethodOptions(InvokeMethodOptions options)
        {
        }

        public IAsyncEnumerable<ChatResponseUpdate> InvokeImplementation() => throw new NotSupportedException();
    }
}
