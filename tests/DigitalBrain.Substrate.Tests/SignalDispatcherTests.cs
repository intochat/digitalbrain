using System.Reflection;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class SignalDispatcherTests
{
    [Fact]
    public void DispatchAsync_HasTheExactInternalSurface()
    {
        var type = typeof(SignalDispatcher);
        Assert.False(type.IsPublic);

        var method = Assert.Single(
            type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly),
            static candidate => candidate.Name == "DispatchAsync");

        Assert.True(method.IsAssembly);
        Assert.False(method.IsStatic);
        Assert.Equal(typeof(Task<DeliveryOutcome>), method.ReturnType);
        Assert.Equal(
            [typeof(object), typeof(Signal), typeof(CancellationToken)],
            method.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task DispatchAsync_MatchingHandlerInvokesAndReturnsHandled()
    {
        var dispatcher = new SignalDispatcher();
        var handler = new RecordingHandler();
        var signal = new Ping("handled");
        using var cancellation = new CancellationTokenSource();

        var outcome = await dispatcher.DispatchAsync(handler, signal, cancellation.Token);

        Assert.Equal(DeliveryOutcome.Handled, outcome);
        Assert.Same(signal, handler.Signal);
        Assert.Equal(cancellation.Token, handler.CancellationToken);
    }

    [Fact]
    public async Task DispatchAsync_MissingHandlerReturnsUnhandled()
    {
        var dispatcher = new SignalDispatcher();

        var outcome = await dispatcher.DispatchAsync(
            new object(),
            new Ping("ignored"),
            CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Unhandled, outcome);
    }

    [Fact]
    public async Task DispatchAsync_HandlerInheritedThroughDomainInterfaceIsDiscovered()
    {
        var dispatcher = new SignalDispatcher();
        var handler = new DomainHandler();
        var signal = new Ping("inherited");

        var outcome = await dispatcher.DispatchAsync(
            handler,
            signal,
            CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Handled, outcome);
        Assert.Same(signal, handler.Signal);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotReturnHandledBeforeHandlerCompletes()
    {
        var dispatcher = new SignalDispatcher();
        var handler = new GatedHandler();

        var dispatch = dispatcher.DispatchAsync(
            handler,
            new Ping("waiting"),
            CancellationToken.None);

        Assert.False(dispatch.IsCompleted);
        handler.Complete();
        Assert.Equal(DeliveryOutcome.Handled, await dispatch);
    }

    [Fact]
    public async Task DispatchAsync_SynchronousHandlerFailureEscapesUnwrapped()
    {
        var dispatcher = new SignalDispatcher();
        var failure = new InvalidOperationException("sentinel");
        var handler = new FaultingHandler(failure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(
                handler,
                new Ping("fault"),
                CancellationToken.None));

        Assert.Same(failure, thrown);
    }

    private sealed class RecordingHandler : IHandle<Ping>
    {
        internal Ping? Signal { get; private set; }

        internal CancellationToken CancellationToken { get; private set; }

        public Task HandleAsync(Ping signal, CancellationToken cancellationToken)
        {
            Signal = signal;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingHandler(Exception failure) : IHandle<Ping>
    {
        public Task HandleAsync(Ping signal, CancellationToken cancellationToken)
            => throw failure;
    }

    private interface IDomainHandler : IHandle<Ping>;

    private sealed class DomainHandler : IDomainHandler
    {
        internal Ping? Signal { get; private set; }

        public Task HandleAsync(Ping signal, CancellationToken cancellationToken)
        {
            Signal = signal;
            return Task.CompletedTask;
        }
    }

    private sealed class GatedHandler : IHandle<Ping>
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(Ping signal, CancellationToken cancellationToken)
            => _completion.Task;

        internal void Complete() => _completion.SetResult();
    }
}
