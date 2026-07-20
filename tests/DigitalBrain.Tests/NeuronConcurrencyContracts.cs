using DigitalBrain.Kernel;
using Orleans.Concurrency;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class NeuronConcurrencyContracts
{
    [Fact]
    public void ActivationGuardCannotBeOverridden()
    {
        var activation = typeof(Neuron).GetMethod(nameof(Neuron.OnActivateAsync))!;

        Assert.True(activation.IsFinal);
    }

    [Fact]
    public void ReentrantNeuronTypesAreRejected()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ReentrantType)));

        Assert.Contains(nameof(ReentrantAttribute), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionallyInterleavingNeuronTypesAreRejected()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ConditionalType)));

        Assert.Contains(nameof(MayInterleaveAttribute), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlwaysInterleavingNeuronMethodsAreRejected()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(AlwaysType)));

        Assert.Contains(nameof(AlwaysInterleaveAttribute), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyNeuronMethodsAreRejected()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ReadOnlyType)));

        Assert.Contains(nameof(ReadOnlyAttribute), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StatelessWorkerNeuronTypesAreRejected()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(StatelessType)));

        Assert.Contains(nameof(StatelessWorkerAttribute), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InterleavingGrainTimersAreRejected()
    {
        var options = new GrainTimerCreationOptions(TimeSpan.Zero, TimeSpan.FromMinutes(1))
        {
            Interleave = true,
        };

        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTimer(options));

        Assert.Contains(nameof(GrainTimerCreationOptions.Interleave), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedNeuronsCannotRegisterInterleavingGrainTimers()
    {
        var neuron = (TimerType)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TimerType));

        var refusal = Assert.Throws<InvalidOperationException>(neuron.RegisterInterleavingTimer);

        Assert.Contains(nameof(GrainTimerCreationOptions.Interleave), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedNeuronsCannotRegisterParameterlessInterleavingGrainTimers()
    {
        var neuron = (TimerType)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TimerType));

        var refusal = Assert.Throws<InvalidOperationException>(neuron.RegisterParameterlessInterleavingTimer);

        Assert.Contains(nameof(GrainTimerCreationOptions.Interleave), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NeuronShadowsEveryOptionsBasedGrainTimerOverload()
    {
        var timerRegistrations = typeof(Neuron)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(method => method.Name == "RegisterGrainTimer")
            .Where(method => method.GetParameters().LastOrDefault()?.ParameterType == typeof(GrainTimerCreationOptions))
            .ToArray();

        Assert.Equal(4, timerRegistrations.Length);
    }

    [Fact]
    public void NeuronRefusesTheLegacyInterleavingTimer()
    {
        var legacyTimer = typeof(TimerType).GetMethod(
            "RegisterTimer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<object, Task>), typeof(object), typeof(TimeSpan), typeof(TimeSpan)],
            modifiers: null)!;

        Assert.Equal(typeof(Neuron), legacyTimer.DeclaringType);

        var neuron = (TimerType)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TimerType));
        var invocation = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => legacyTimer.Invoke(
                neuron,
                [new Func<object, Task>(static _ => Task.CompletedTask), new object(), TimeSpan.Zero, TimeSpan.FromMinutes(1)]));

        Assert.IsType<InvalidOperationException>(invocation.InnerException);
    }

    [Reentrant]
    private sealed class ReentrantType;

    [MayInterleave("CanInterleave")]
    private sealed class ConditionalType;

    private interface IAlwaysInterleaving
    {
        [AlwaysInterleave]
        Task InterleaveAsync();
    }

    private sealed class AlwaysType : IAlwaysInterleaving
    {
        public Task InterleaveAsync() => Task.CompletedTask;
    }

    private interface IReadOnlyInterleaving
    {
        [ReadOnly]
        Task ReadAsync();
    }

    private sealed class ReadOnlyType : IReadOnlyInterleaving
    {
        public Task ReadAsync() => Task.CompletedTask;
    }

    [StatelessWorker]
    private sealed class StatelessType;

    private sealed class TimerType : Neuron
    {
        internal IGrainTimer RegisterInterleavingTimer()
            => RegisterGrainTimer(
                static _ => Task.CompletedTask,
                new GrainTimerCreationOptions(TimeSpan.Zero, TimeSpan.FromMinutes(1))
                {
                    Interleave = true,
                });

        internal IGrainTimer RegisterParameterlessInterleavingTimer()
            => RegisterGrainTimer(
                static () => Task.CompletedTask,
                new GrainTimerCreationOptions(TimeSpan.Zero, TimeSpan.FromMinutes(1))
                {
                    Interleave = true,
                });
    }
}
