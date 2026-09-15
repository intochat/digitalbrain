using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Core.Behaviors;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorRecoveryFacts
{
    private static readonly NeuronId Source = NeuronId.Plain("recovery-source");
    private static readonly NeuronId Target = new("counter", "recovery-target");
    private static readonly NeuronId BehaviorId = new("behavior", "action-recovery");

    [Theory]
    [InlineData("unknown", 1)]
    [InlineData("completed", 1)]
    [InlineData("missing", 0)]
    public async Task Cold_restart_pauses_uncertain_action_preserves_later_inputs_and_never_repeats_effect(string failureMode, int expectedExecutions)
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        var fixture = new CounterFixtureState();
        var original = SignalDelivery.Create(Signal.Create("Add", """{"count":1,"note":"first"}"""), Source, 1, TimeProvider.System);
        NeuronId actionId;
        CommandId commandId;
        await using (var brain = await Start(directory, fixture, new ActionReplyFault(failureMode)))
        {
            var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
            var input = new PayloadContract("Add", InputSchema(brain));
            var definition = new BehaviorDefinition("uncertain action",
                [new("source", "source", "{}", Output: input, SharedNeuron: Source),
                 new("action", "action", """{"target":"counter:recovery-target","interface":"test.counter","method":"add"}""", input)],
                [new("source", "action")]);
            await behavior.Save(new(definition, CommandId.New()));
            await ReactionWait.UntilAsync(async () => (await behavior.Read()).Version == 1, TestContext.Current.CancellationToken);
            await behavior.Start(new(CommandId.New()));
            await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Running, TestContext.Current.CancellationToken);
            actionId = (await behavior.Read()).Bindings.Single(binding => binding.Role == "action").Neuron;
            commandId = BehaviorMapping.ActionId(actionId, original.SignalId);
            if (failureMode == "unknown")
            {
                // The fixture effect happens in the command callback, then this hook loses its
                // terminal persist. Disk retains Attempted; activation reconciliation yields Unknown.
                fixture.LostTurns[commandId] = Target.Name;
            }
            var action = brain.Grains.GetGrain<INeuron>(actionId.ToGrainId());
            await action.Deliver(original, TestContext.Current.CancellationToken);
            var processor = brain.Grains.GetGrain<IBehaviorNode>(actionId.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await processor.Status()).Error is not null, TestContext.Current.CancellationToken);
            Assert.Equal(commandId.ToString(), (await processor.Status()).UncertainAction);
            Assert.Equal(expectedExecutions, fixture.Executions.GetValueOrDefault(Target.Name));
            Assert.Empty((await action.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        }

        await using (var brain = await Start(directory, fixture, new ActionReplyFault(null)))
        {
            var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
            var action = brain.Grains.GetGrain<INeuron>(actionId.ToGrainId());
            var processor = brain.Grains.GetGrain<IBehaviorNode>(actionId.ToGrainId());
            var status = await processor.Status();
            Assert.Equal(commandId.ToString(), status.UncertainAction);
            Assert.NotNull(status.Error);
            if (failureMode != "completed")
            {
                Assert.Contains("uncertain outcome", status.Error, StringComparison.Ordinal);
            }
            Assert.Equal(DeliveryAdmission.Duplicate, await action.Deliver(original, TestContext.Current.CancellationToken));
            var second = SignalDelivery.Create(Signal.Create("Add", """{"count":2,"note":"later"}"""), Source, 2, TimeProvider.System);
            Assert.Equal(DeliveryAdmission.Accepted, await action.Deliver(second, TestContext.Current.CancellationToken));
            await brain.Grains.GetGrain<INeuronInbox>(actionId.ToGrainId()).Drain();
            Assert.Equal(1, await action.ReadPendingCount());
            Assert.Equal(expectedExecutions, fixture.Executions.GetValueOrDefault(Target.Name));
            Assert.Empty((await action.ReadJournal(JournalKind.Outgoing, 0)).Delta);

            var counter = brain.Grains.GetGrain<ICounter>(Target.ToGrainId());
            if (failureMode == "completed")
            {
                await ReactionWait.UntilAsync(async () => await counter.ReadTotal() == 1, TestContext.Current.CancellationToken);
            }
            var journal = await counter.ReadCommands(0);
            if (failureMode == "missing")
            {
                Assert.Empty(journal.Delta);
            }
            else
            {
                Assert.Equal(commandId, Assert.Single(journal.Delta.Select(record => record.Id).Distinct()));
                Assert.Single(journal.Delta, record => record.Phase == CommandPhase.Attempted);
                Assert.Contains(journal.Delta, record => record.Phase == (failureMode == "unknown" ? CommandPhase.Unknown : CommandPhase.Completed));
                Assert.DoesNotContain(journal.Delta, record => record.Incarnation > 1);
            }

            await behavior.Stop(new(CommandId.New()));
            await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Stopped, TestContext.Current.CancellationToken);
            Assert.False((await processor.Status()).Enabled);
            var third = SignalDelivery.Create(Signal.Create("Add", """{"count":3,"note":"after stop"}"""), Source, 3, TimeProvider.System);
            await action.Deliver(third, TestContext.Current.CancellationToken);
            await ReactionWait.UntilAsync(async () => await action.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
            Assert.Equal(expectedExecutions, fixture.Executions.GetValueOrDefault(Target.Name));
            Assert.Empty((await action.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Busy_retries_only_when_the_target_journal_proves_no_command_was_attempted(bool afterAttempt)
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        var fixture = new CounterFixtureState();
        var fault = new BusyReplyFault(afterAttempt);
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = directory,
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton(fixture);
                silo.Services.AddSingleton<ICommandCrashPoint, FixtureCommandCrashPoint>();
                silo.Services.AddSingleton<IIncomingGrainCallFilter>(fault);
            }
        });
        var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
        var input = new PayloadContract("Add", InputSchema(brain));
        var definition = new BehaviorDefinition("busy action",
            [new("source", "source", "{}", Output: input, SharedNeuron: Source),
             new("action", "action", """{"target":"counter:recovery-target","interface":"test.counter","method":"add"}""", input)],
            [new("source", "action")]);
        await behavior.Save(new(definition, CommandId.New()));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Version == 1, TestContext.Current.CancellationToken);
        await behavior.Start(new(CommandId.New()));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Running, TestContext.Current.CancellationToken);
        var actionId = (await behavior.Read()).Bindings.Single(binding => binding.Role == "action").Neuron;
        var delivery = SignalDelivery.Create(Signal.Create("Add", """{"count":1,"note":"busy"}"""), Source, 1, TimeProvider.System);
        var commandId = BehaviorMapping.ActionId(actionId, delivery.SignalId);
        if (afterAttempt)
        {
            fixture.LostTurns[commandId] = Target.Name;
        }
        var action = brain.Grains.GetGrain<INeuron>(actionId.ToGrainId());
        var processor = brain.Grains.GetGrain<IBehaviorNode>(actionId.ToGrainId());
        await action.Deliver(delivery, TestContext.Current.CancellationToken);
        await ReactionWait.UntilAsync(async () => (await processor.Status()).Error is not null
            || (fixture.Executions.GetValueOrDefault(Target.Name) == 1 && await action.ReadPendingCount() == 0), TestContext.Current.CancellationToken);
        var status = await processor.Status();
        Assert.Equal(1, fixture.Executions.GetValueOrDefault(Target.Name));
        var journal = await brain.Grains.GetGrain<ICounter>(Target.ToGrainId()).ReadCommands(0);
        Assert.Single(journal.Delta, record => record.Phase == CommandPhase.Attempted);
        if (afterAttempt)
        {
            Assert.NotNull(status.Error);
            Assert.Contains("uncertain outcome", status.Error, StringComparison.Ordinal);
            Assert.Equal(commandId.ToString(), status.UncertainAction);
            Assert.Equal(1, fault.Calls);
        }
        else
        {
            Assert.Null(status.Error);
            Assert.Null(status.UncertainAction);
            Assert.Contains(journal.Delta, record => record.Phase == CommandPhase.Completed);
            Assert.Equal(2, fault.Calls);
        }
    }

    private sealed class BusyReplyFault(bool afterAttempt) : IIncomingGrainCallFilter
    {
        public int Calls { get; private set; }

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            if (context.Grain is CounterNeuron && context.InterfaceMethod.Name == nameof(ICounter.Add) && ++Calls == 1)
            {
                if (afterAttempt)
                {
                    try
                    {
                        await context.Invoke();
                    }
                    catch (NeuronPersistenceException)
                    {
                        // The command callback ran, but its terminal write was lost.
                    }
                }
                throw new NeuronBusyException("Simulated target backpressure.");
            }
            await context.Invoke();
        }
    }

    private static string InputSchema(BrainSimulation brain)
    {
        var invoker = brain.SiloServices.GetRequiredService<INeuronInvoker>();
        var method = invoker.Describe(Target).Single(descriptor => descriptor.InterfaceAlias == "test.counter" && descriptor.MethodAlias == "add");
        var schema = JsonNode.Parse(method.ArgsSchema!.Value.GetRawText())!.AsObject();
        var identity = invoker.ArgumentContractOf("test.counter", "add")!.CommandIdPropertyName!;
        schema["properties"]!.AsObject().Remove(identity);
        var required = schema["required"]!.AsArray();
        for (var index = required.Count - 1; index >= 0; index--)
        {
            if (required[index]!.GetValue<string>() == identity)
            {
                required.RemoveAt(index);
            }
        }
        return schema.ToJsonString();
    }

    private static Task<BrainSimulation> Start(string directory, CounterFixtureState fixture, ActionReplyFault fault) => BrainSimulation.StartAsync(new()
    {
        Modules = new([]),
        PersistenceDirectory = directory,
        ConfigureSilo = silo =>
        {
            silo.Services.AddSingleton(fixture);
            silo.Services.AddSingleton<ICommandCrashPoint, FixtureCommandCrashPoint>();
            silo.Services.AddSingleton<IIncomingGrainCallFilter>(fault);
        }
    });

    private sealed class ActionReplyFault(string? mode) : IIncomingGrainCallFilter
    {
        private bool _fired;
        public async Task Invoke(IIncomingGrainCallContext context)
        {
            if (!_fired && context.Grain is CounterNeuron && context.InterfaceMethod.Name == nameof(ICounter.Add) && mode is "missing" or "completed")
            {
                _fired = true;
                if (mode == "missing")
                {
                    throw new NeuronPersistenceException(Target, "simulated lost dispatch", new IOException("No target command was admitted."));
                }
                await context.Invoke();
                throw new TimeoutException("Target completed but its response was lost.");
            }
            await context.Invoke();
        }
    }
}


