using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IncomingReificationFilterContracts
{
    [Fact(DisplayName = "the incoming filter rejects an attributed no-context semantic call before target invocation")]
    public async Task AttributedNoContextSemanticCallIsRejectedBeforeTargetInvocation()
    {
        RequestContext.Clear();

        var target = (IncomingTarget)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(IncomingTarget));
        var context = new IncomingContext(
            target,
            GrainId.Create("raw-runner", "incoming-default-deny/runner"),
            typeof(ITask).GetMethod(nameof(ITask.ReadAsync))!);

        var failure = await Record.ExceptionAsync(
            () => new IncomingReificationFilter().Invoke(context));

        Assert.False(context.WasInvoked);
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    private sealed class IncomingTarget : Neuron;

    private sealed class IncomingContext(
        object grain,
        GrainId sourceId,
        MethodInfo interfaceMethod) : IIncomingGrainCallContext
    {
        internal bool WasInvoked { get; private set; }

        public IGrainContext TargetContext => throw new NotSupportedException();

        public MethodInfo ImplementationMethod => interfaceMethod;

        public IInvokable Request => throw new NotSupportedException();

        public object Grain => grain;

        public GrainId? SourceId => sourceId;

        public GrainId TargetId => GrainId.Create("task", "incoming-default-deny/target");

        public GrainInterfaceType InterfaceType => default;

        public string InterfaceName => interfaceMethod.DeclaringType!.FullName!;

        public string MethodName => interfaceMethod.Name;

        public MethodInfo InterfaceMethod => interfaceMethod;

        public object? Result { get; set; }

        public Response? Response { get; set; }

        public Task Invoke()
        {
            WasInvoked = true;

            return Task.CompletedTask;
        }
    }
}
