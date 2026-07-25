using System.Diagnostics;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    protected IGrainTimer RegisterGrainTimer(
        Func<CancellationToken, Task> callback,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, options);
    }

    protected IGrainTimer RegisterGrainTimer(
        Func<Task> callback,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, options);
    }

    protected IGrainTimer RegisterGrainTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, state, options);
    }

    protected IGrainTimer RegisterGrainTimer<TState>(
        Func<TState, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, state, options);
    }

    protected new IDisposable RegisterTimer(
        Func<object, Task> callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period)
        => throw new InvalidOperationException(
            $"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require serialized turns.");

}
