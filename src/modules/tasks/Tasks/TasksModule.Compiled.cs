using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tasks;

public sealed partial class TasksModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Tasks.TasksModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "TasksModule module",
            [
                new NeuronCapabilityDescriptor(
                    "tasks.task",
                    "Durable task lifecycle neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "tasks.attempt-accepted",
                            1,
                            "Worker accepted a task attempt",
                            CapabilitySchema.For(typeof(AttemptAccepted))),
                        new SynapseCapabilityDescriptor(
                            "tasks.attempt-cancelled",
                            1,
                            "Worker cancelled a task attempt",
                            CapabilitySchema.For(typeof(AttemptCancelled))),
                        new SynapseCapabilityDescriptor(
                            "tasks.attempt-failed",
                            1,
                            "Worker failed a task attempt",
                            CapabilitySchema.For(typeof(AttemptFailed))),
                        new SynapseCapabilityDescriptor(
                            "tasks.attempt-outcome-uncertain",
                            1,
                            "Worker reported an uncertain task attempt outcome",
                            CapabilitySchema.For(typeof(AttemptOutcomeUncertain))),
                        new SynapseCapabilityDescriptor(
                            "tasks.attempt-progressed",
                            1,
                            "Worker reported progress on a task attempt",
                            CapabilitySchema.For(typeof(AttemptProgressed))),
                        new SynapseCapabilityDescriptor(
                            "tasks.attempt-succeeded",
                            1,
                            "Worker completed a task attempt successfully",
                            CapabilitySchema.For(typeof(AttemptSucceeded))),
                        new SynapseCapabilityDescriptor(
                            "tasks.attempt-waiting",
                            1,
                            "Worker is blocked waiting on a task attempt",
                            CapabilitySchema.For(typeof(AttemptWaiting))),
                        new SynapseCapabilityDescriptor(
                            "tasks.complete-user-action",
                            1,
                            "Bridge-owned completion of a parked module user action",
                            CapabilitySchema.For(typeof(CompleteUserAction))),
                        new SynapseCapabilityDescriptor(
                            "tasks.deny-user-action",
                            1,
                            "Bridge-owned denial of a parked module user action",
                            CapabilitySchema.For(typeof(DenyUserAction))),
                        new SynapseCapabilityDescriptor(
                            "tasks.prepare-operation",
                            1,
                            "Prepare a directed task operation at a deterministic attempt sequence",
                            CapabilitySchema.For(typeof(PrepareTaskOperation))),
                        new SynapseCapabilityDescriptor(
                            "tasks.read-operation",
                            1,
                            "Read a durable task operation snapshot by attempt sequence",
                            CapabilitySchema.For(typeof(ReadTaskOperation))),
                        new SynapseCapabilityDescriptor(
                            "tasks.start",
                            1,
                            "Start a durable owner-scoped task",
                            CapabilitySchema.For(typeof(StartTask))),
                        new SynapseCapabilityDescriptor(
                            "tasks.transition-operation",
                            1,
                            "Transition a prepared or in-flight task operation to the next durable phase",
                            CapabilitySchema.For(typeof(TransitionTaskOperation))),
                        new SynapseCapabilityDescriptor(
                            "tasks.user-action-required",
                            1,
                            "Module-owned user action required before a task attempt can continue",
                            CapabilitySchema.For(typeof(UserActionRequired))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "tasks.snapshot",
                            1,
                            "Durable task start result and lifecycle snapshot",
                            CapabilitySchema.For(typeof(TaskSnapshot))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(TasksModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
