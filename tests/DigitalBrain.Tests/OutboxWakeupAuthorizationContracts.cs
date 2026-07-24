using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class OutboxWakeupAuthorizationContracts
{
    [Fact]
    public async Task ExactWakeupForTargetMayInvokeItsDrain()
    {
        var target = new NeuronId(
            "authorization-target",
            new OwnerId("outbox-authorization"),
            "target");
        var context = new OutboxDrainContext(
            GrainId.Create("db-outbox-wakeup", target.ToString()),
            target.ToGrainId());

        await new OwnerBoundCallFilter([]).Invoke(context);

        Assert.True(context.WasInvoked);
    }

    [Theory]
    [InlineData("unattributed")]
    [InlineData("wrong-helper")]
    [InlineData("malformed-key")]
    [InlineData("mismatched-target")]
    public async Task AnyWakeupMismatchRejectsBeforeDrainInvocation(
        string mismatch)
    {
        var target = new NeuronId(
            "authorization-target",
            new OwnerId($"outbox-{mismatch}"),
            "target");
        var encoded = mismatch == "mismatched-target"
            ? new NeuronId(
                target.Type,
                target.Owner,
                "other").ToString()
            : target.ToString();
        GrainId? source = mismatch switch
        {
            "unattributed" => null,
            "wrong-helper" => GrainId.Create(
                "another-helper",
                encoded),
            "malformed-key" => GrainId.Create(
                "db-outbox-wakeup",
                "missing-colon"),
            _ => GrainId.Create(
                "db-outbox-wakeup",
                encoded),
        };
        var context = new OutboxDrainContext(
            source,
            target.ToGrainId());

        var failure = await Record.ExceptionAsync(
            () => new OwnerBoundCallFilter([]).Invoke(context));

        Assert.False(context.WasInvoked);
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    private sealed class OutboxDrainContext(
        GrainId? source,
        GrainId target) : IIncomingGrainCallContext
    {
        private static readonly MethodInfo Drain = typeof(IOutboxDrain)
            .GetMethod(nameof(IOutboxDrain.Drain))!;

        internal bool WasInvoked { get; private set; }

        public IGrainContext TargetContext =>
            throw new NotSupportedException();

        public MethodInfo ImplementationMethod => Drain;

        public IInvokable Request { get; } =
            new MethodInvokable(Drain);

        public object Grain { get; } = new();

        public GrainId? SourceId => source;

        public GrainId TargetId => target;

        public GrainInterfaceType InterfaceType => default;

        public string InterfaceName => typeof(IOutboxDrain).FullName!;

        public string MethodName => nameof(IOutboxDrain.Drain);

        public MethodInfo InterfaceMethod => Drain;

        public object? Result { get; set; }

        public Response? Response { get; set; }

        public Task Invoke()
        {
            WasInvoked = true;
            return Task.CompletedTask;
        }
    }

    private sealed class MethodInvokable(MethodInfo method) : IInvokable
    {
        public int GetArgumentCount() => 0;

        public object? GetArgument(int index) =>
            throw new ArgumentOutOfRangeException(nameof(index));

        public void SetArgument(int index, object value) =>
            throw new NotSupportedException();

        public string GetMethodName() => method.Name;

        public string GetInterfaceName() => method.DeclaringType!.FullName!;

        public Type GetInterfaceType() => method.DeclaringType!;

        public MethodInfo GetMethod() => method;

        public string GetActivityName() => method.Name;

        public TimeSpan? GetDefaultResponseTimeout() => null;

        public void SetTarget(ITargetHolder holder) =>
            throw new NotSupportedException();

        public object? GetTarget() => null;

        public ValueTask<Response> Invoke() =>
            throw new NotSupportedException();

        public bool IsCancellable => false;

        public bool TryCancel() => false;

        public CancellationToken GetCancellationToken() =>
            CancellationToken.None;

        public void Dispose()
        {
        }
    }
}
