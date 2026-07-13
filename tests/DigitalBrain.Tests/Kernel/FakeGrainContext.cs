using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Tests.Kernel;

internal sealed class FakeGrainContext(IServiceProvider services) : IGrainContext
{
    public IServiceProvider ActivationServices => services;
    public GrainReference GrainReference => throw new NotSupportedException();
    public GrainId GrainId => default;
    public object? GrainInstance => null;
    public ActivationId ActivationId => default;
    public GrainAddress Address => throw new NotSupportedException();
    public IGrainLifecycle ObservableLifecycle => throw new NotSupportedException();
    public IWorkItemScheduler Scheduler => throw new NotSupportedException();
    public Task Deactivated => throw new NotSupportedException();

    public void SetComponent<TComponent>(TComponent? value) where TComponent : class => throw new NotSupportedException();
    public void ReceiveMessage(object message) => throw new NotSupportedException();
    public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void Rehydrate(IRehydrationContext context) => throw new NotSupportedException();
    public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    object? ITargetHolder.GetTarget() => throw new NotSupportedException();
    object? ITargetHolder.GetComponent(Type componentType) => throw new NotSupportedException();
    bool IEquatable<IGrainContext>.Equals(IGrainContext? other) => ReferenceEquals(this, other);
}
