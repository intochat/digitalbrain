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
            typeof(ITask).GetMethod(nameof(ITask.Read))!);

        var failure = await Record.ExceptionAsync(
            () => new IncomingReificationFilter().Invoke(context));

        Assert.False(context.WasInvoked);
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    [Fact(DisplayName = "the authority callback rejects a runner targeting the wrong causal caller")]
    public async Task AuthorityCallbackRejectsWrongCausalCallerBeforeInvocation()
    {
        var owner = new OwnerId("authority-callback");
        var causalCaller = new NeuronId("incomingtarget", owner, "issuer");
        var requestedTarget = new NeuronId("task", owner, "semantic-target");
        var authorizedRunner = GrainId.Create("runner", $"{owner.Value}/authorized");
        var request = SynapseDelivery.Create(
            new CapabilityRequested("DigitalBrain.Tasks.ITask", "Read", requestedTarget),
            causalCaller,
            sequence: 1);
        var delegation = new CapabilityDelegation(
            Guid.NewGuid(),
            request,
            authorizedRunner,
            owner);
        var target = (IncomingTarget)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(IncomingTarget));
        var context = new IncomingContext(
            target,
            authorizedRunner,
            typeof(ICapabilityDelegationAuthority).GetMethod(
                nameof(ICapabilityDelegationAuthority.RedeemAsync))!,
            GrainId.Create("incomingtarget", $"{owner.Value}/wrong-caller"),
            delegation);

        var failure = await Record.ExceptionAsync(
            () => new IncomingReificationFilter().Invoke(context));

        Assert.False(context.WasInvoked);
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    [Fact(DisplayName = "the authority callback rejects the wrong actual runner source")]
    public async Task AuthorityCallbackRejectsWrongActualRunnerBeforeInvocation()
    {
        var owner = new OwnerId("authority-source");
        var causalCaller = new NeuronId("incomingtarget", owner, "issuer");
        var requestedTarget = new NeuronId("task", owner, "semantic-target");
        var authorizedRunner = GrainId.Create("runner", $"{owner.Value}/authorized");
        var request = SynapseDelivery.Create(
            new CapabilityRequested("DigitalBrain.Tasks.ITask", "Read", requestedTarget),
            causalCaller,
            sequence: 1);
        var delegation = new CapabilityDelegation(
            Guid.NewGuid(),
            request,
            authorizedRunner,
            owner);
        var target = (IncomingTarget)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(IncomingTarget));
        var context = new IncomingContext(
            target,
            GrainId.Create("runner", $"{owner.Value}/wrong"),
            typeof(ICapabilityDelegationAuthority).GetMethod(
                nameof(ICapabilityDelegationAuthority.RedeemAsync))!,
            causalCaller.ToGrainId(),
            delegation);

        var failure = await Record.ExceptionAsync(
            () => new IncomingReificationFilter().Invoke(context));

        Assert.False(context.WasInvoked);
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    [Theory(DisplayName = "a redeemed delegation is revalidated against each actual incoming dimension")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("contract")]
    [InlineData("method")]
    public async Task RedeemedDelegationIsRevalidatedAgainstActualIncomingCall(string mismatch)
    {
        RequestContext.Clear();

        var owner = new OwnerId($"incoming-redeemed-{mismatch}");
        var causalCaller = new NeuronId("incomingtarget", owner, "issuer");
        var requestedTarget = new NeuronId("task", owner, "target");
        var authorizedRunner = GrainId.Create("runner", $"{owner.Value}/authorized");
        var request = SynapseDelivery.Create(
            new CapabilityRequested(
                typeof(ITask).FullName!,
                nameof(ITask.Read),
                requestedTarget),
            causalCaller,
            sequence: 1);
        var delegation = new CapabilityDelegation(
            Guid.NewGuid(),
            request,
            authorizedRunner,
            owner);
        var target = (IncomingTarget)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(IncomingTarget));
        var actualSource = mismatch == "source"
            ? GrainId.Create("runner", $"{owner.Value}/wrong")
            : authorizedRunner;
        var actualTarget = mismatch == "target"
            ? GrainId.Create("task", $"{owner.Value}/wrong")
            : requestedTarget.ToGrainId();
        var actualMethod = mismatch switch
        {
            "contract" => typeof(IAlternateTaskCapability).GetMethod(
                nameof(IAlternateTaskCapability.ReadAsync))!,
            "method" => typeof(ITask).GetMethod(nameof(ITask.Cancel))!,
            _ => typeof(ITask).GetMethod(nameof(ITask.Read))!,
        };
        var context = new IncomingContext(
            target,
            actualSource,
            actualMethod,
            actualTarget);

        var failure = await Record.ExceptionAsync(
            () => CapabilityRequestContext.InvokeRedeemedAsync(
                delegation,
                () => new IncomingReificationFilter().Invoke(context)));

        Assert.False(context.WasInvoked);
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    private sealed class IncomingTarget : Neuron;

    private sealed class IncomingContext(
        object grain,
        GrainId sourceId,
        MethodInfo interfaceMethod,
        GrainId? targetId = null,
        object? argument = null) : IIncomingGrainCallContext
    {
        internal bool WasInvoked { get; private set; }

        public IGrainContext TargetContext => throw new NotSupportedException();

        public MethodInfo ImplementationMethod => interfaceMethod;

        public IInvokable Request { get; } = new ArgumentInvokable(interfaceMethod, argument);

        public object Grain => grain;

        public GrainId? SourceId => sourceId;

        public GrainId TargetId { get; } = targetId
            ?? GrainId.Create("task", "incoming-default-deny/target");

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

    private sealed class ArgumentInvokable(MethodInfo method, object? argument) : IInvokable
    {
        public int GetArgumentCount() => argument is null ? 0 : 1;

        public object? GetArgument(int index)
            => index == 0 && argument is not null
                ? argument
                : throw new ArgumentOutOfRangeException(nameof(index));

        public void SetArgument(int index, object value) => throw new NotSupportedException();

        public string GetMethodName() => method.Name;

        public string GetInterfaceName() => method.DeclaringType!.FullName!;

        public Type GetInterfaceType() => method.DeclaringType!;

        public MethodInfo GetMethod() => method;

        public string GetActivityName() => method.Name;

        public TimeSpan? GetDefaultResponseTimeout() => null;

        public void SetTarget(ITargetHolder holder) => throw new NotSupportedException();

        public object? GetTarget() => null;

        public ValueTask<Response> Invoke() => throw new NotSupportedException();

        public bool IsCancellable => false;

        public bool TryCancel() => false;

        public CancellationToken GetCancellationToken() => CancellationToken.None;

        public void Dispose()
        {
        }
    }
}

internal partial interface IAlternateTaskCapability : INeuron
{
    Task<TaskSnapshot> ReadAsync();
}
