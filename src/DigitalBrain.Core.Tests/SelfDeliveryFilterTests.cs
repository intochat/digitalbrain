using System.Reflection;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Core.Tests;

public sealed class SelfDeliveryFilterTests
{
    [Fact(DisplayName = "An outgoing grain call that targets the same activation throws naming the self-delivery rule")]
    public async Task ProxiedSelfCallThrowsNamingTheRule()
    {
        var grainId = GrainId.Create("probe", "self");
        var source = new StubGrainContext(grainId);
        var self = new StubOutgoingContext(source, grainId);
        var other = new StubOutgoingContext(source, GrainId.Create("probe", "other"));
        var filter = new OutgoingSynapseFilter();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => filter.Invoke(self));

        Assert.Contains("proxied self-call", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("self-delivery", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(self.Invoked);

        // Same filter: a different activation is not blocked (positive half of the self-delivery rule).
        await filter.Invoke(other);
        Assert.True(other.Invoked);
    }

    private sealed class StubGrainContext(GrainId grainId) : IGrainContext
    {
        public GrainReference GrainReference => throw new NotSupportedException();

        public GrainId GrainId { get; } = grainId;

        public object? GrainInstance => null;

        public ActivationId ActivationId => throw new NotSupportedException();

        public GrainAddress Address => throw new NotSupportedException();

        public IServiceProvider ActivationServices => throw new NotSupportedException();

        public IGrainLifecycle ObservableLifecycle => throw new NotSupportedException();

        public IWorkItemScheduler Scheduler => throw new NotSupportedException();

        public Task Deactivated => throw new NotSupportedException();

        public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Deactivate(DeactivationReason reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public object? GetTarget() => null;

        public object? GetComponent(Type componentType) => GrainId.IsDefault ? null : null;

        public TComponent? GetComponent<TComponent>() where TComponent : class
            => GrainId.IsDefault ? null : null;

        public void SetComponent<TComponent>(TComponent? value) where TComponent : class
            => throw new NotSupportedException();

        public void ReceiveMessage(object message) => throw new NotSupportedException();

        public void Rehydrate(IRehydrationContext context) => throw new NotSupportedException();

        public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public bool Equals(IGrainContext? other) => other is not null && GrainId.Equals(other.GrainId);
    }

    private sealed class StubOutgoingContext(IGrainContext? source, GrainId targetId) : IOutgoingGrainCallContext
    {
        public bool Invoked { get; private set; }

        public IGrainContext? SourceContext { get; } = source;

        public GrainId TargetId { get; } = targetId;

        public IInvokable Request => throw new NotSupportedException();

        public object Grain => throw new NotSupportedException();

        public GrainId? SourceId => SourceContext?.GrainId;

        public GrainInterfaceType InterfaceType => default;

        public string InterfaceName => "IProbe";

        public string MethodName => "Ping";

        public MethodInfo InterfaceMethod => null!;

        public object? Result { get; set; }

        public Response? Response { get; set; }

        public Task Invoke()
        {
            Invoked = true;
            return Task.CompletedTask;
        }
    }
}
