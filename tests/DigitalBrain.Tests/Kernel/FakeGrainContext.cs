using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Tests.Kernel;

// Minimal IGrainContext fake — Orleans.TestingHost (10.2.0) has no ready-made fake grain context to
// reuse, so this hand-rolls the full member list of the installed Orleans.Runtime.IGrainContext
// (verified via reflection against the restored 10.2.1-preview.1 assembly, not guessed), throwing
// NotSupportedException for everything this test doesn't exercise beyond ActivationServices.
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
