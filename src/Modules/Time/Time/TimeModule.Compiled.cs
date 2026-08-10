using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Time;

public sealed partial class TimeModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Time.TimeModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "TimeModule module",
            [
                new NeuronCapabilityDescriptor(
                    "timer",
                    "Owner timer neuron: arms a countdown and fires its note when due",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "time.start-timer",
                            1,
                            "Arm the timer for a number of seconds; the note is delivered when it elapses",
                            CapabilitySchema.For(typeof(StartTimer))),
                        new SynapseCapabilityDescriptor(
                            "time.cancel-timer",
                            1,
                            "Cancel the scheduled timer before it elapses",
                            CapabilitySchema.For(typeof(CancelTimer))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "time.timer-scheduled",
                            1,
                            "The timer is armed; carries the due instant and the note it will deliver",
                            CapabilitySchema.For(typeof(TimerScheduled))),
                        new SynapseCapabilityDescriptor(
                            "time.timer-elapsed",
                            1,
                            "The timer reached its due instant and delivers its note",
                            CapabilitySchema.For(typeof(TimerElapsed))),
                        new SynapseCapabilityDescriptor(
                            "time.timer-cancelled",
                            1,
                            "The scheduled timer was cancelled before it elapsed",
                            CapabilitySchema.For(typeof(TimerCancelled))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(TimeModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
