using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorFacts
{
    private const string InputSchema = """{"type":"object","properties":{"label":{"type":"string"},"value":{"type":"number"}},"required":["label","value"],"additionalProperties":false}""";
    private static readonly NeuronId Source = NeuronId.Plain("behavior-input");
    private static readonly NeuronId Chart = new("chart", "behavior-output");

    [Fact]
    public async Task Filter_map_action_graph_persists_reacts_after_restart_and_stops_its_connections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        BehaviorSnapshot running;
        await using (var brain = await StartBrain(directory))
        {
            var behavior = Behavior(brain);
            var definition = ChartPipeline(brain);
            running = await SaveAndStart(behavior, definition);
            Assert.Equal(4, running.Bindings.Count);
            var source = brain.Grains.GetGrain<INeuron>(Source.ToGrainId());
            await source.Fire(Signal.Create("Observation", """{"label":"ignored","value":-1}"""), null, null, TestContext.Current.CancellationToken);
            await source.Fire(Signal.Create("Observation", """{"label":"first","value":2}"""), null, null, TestContext.Current.CancellationToken);
            var chart = brain.Grains.GetGrain<IChart>(Chart.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await chart.Read()).Points.Count == 1, TestContext.Current.CancellationToken);
            Assert.Equal("first", Assert.Single((await chart.Read()).Points).Label);
        }

        await using (var brain = await StartBrain(directory))
        {
            var behavior = Behavior(brain);
            var restored = await behavior.Read();
            Assert.Equal(BehaviorStatus.Running, restored.Status);
            Assert.Equal(running.RunId, restored.RunId);
            Assert.Equal(running.Bindings, restored.Bindings);
            var source = brain.Grains.GetGrain<INeuron>(Source.ToGrainId());
            await source.Fire(Signal.Create("Observation", """{"label":"second","value":3}"""), null, null, TestContext.Current.CancellationToken);
            var chart = brain.Grains.GetGrain<IChart>(Chart.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await chart.Read()).Points.Count == 2, TestContext.Current.CancellationToken);
            await behavior.Stop(new(CommandId.New()));
            await WaitStatus(behavior, BehaviorStatus.Stopped);
            Assert.Empty(await source.ReadSynapses());
            foreach (var binding in restored.Bindings.Where(binding => binding.Owned))
            {
                Assert.False((await brain.Grains.GetGrain<IBehaviorNode>(binding.Neuron.ToGrainId()).Status()).Enabled);
                Assert.Empty(await brain.Grains.GetGrain<INeuron>(binding.Neuron.ToGrainId()).ReadSynapses());
            }
        }
    }

    [Fact]
    public async Task Stop_preserves_a_manual_connection_sharing_the_same_physical_edge()
    {
        await using var brain = await StartBrain();
        var behavior = Behavior(brain);
        var running = await SaveAndStart(behavior, ChartPipeline(brain));
        var target = running.Bindings.Single(binding => binding.Role == "filter").Neuron;
        var source = brain.Grains.GetGrain<INeuron>(Source.ToGrainId());
        await source.Connect(target, "Observation");
        await behavior.Stop(new(CommandId.New()));
        await WaitStatus(behavior, BehaviorStatus.Stopped);
        Assert.Equal(target, Assert.Single(await source.ReadSynapses()).Target);
        Assert.False((await brain.Grains.GetGrain<IBehaviorNode>(target.ToGrainId()).Status()).Enabled);
    }

    [Fact]
    public async Task Incompatible_edge_is_rejected_before_installing_any_resources()
    {
        await using var brain = await StartBrain();
        var behavior = Behavior(brain);
        var valid = ChartPipeline(brain);
        var nodes = valid.Nodes.ToArray();
        nodes[0] = nodes[0] with { Output = new("WrongSignal", InputSchema) };
        var invalid = valid with { Nodes = nodes };
        var validation = await behavior.Validate(invalid);
        Assert.False(validation.Valid);
        Assert.Contains(validation.Errors, error => error.Contains("incompatible", StringComparison.Ordinal));
        await Assert.ThrowsAsync<CommandRejectedException>(() => behavior.Save(new(invalid, CommandId.New())));
        var state = await behavior.Read();
        Assert.Equal(BehaviorStatus.Draft, state.Status);
        Assert.Null(state.Definition);
        Assert.Empty(state.Bindings);
        Assert.Empty(await brain.Grains.GetGrain<INeuron>(Source.ToGrainId()).ReadSynapses());
        Assert.Empty((await brain.Grains.GetGrain<IChart>(Chart.ToGrainId()).Read()).Points);
    }

    [Fact]
    public async Task Invalid_model_output_pauses_decision_and_does_not_emit_or_call_again()
    {
        using var model = new ScriptedChatClient();
        model.Say("""{"label":"invalid","value":"not a number"}""");
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(UIModule), typeof(AIModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(model)
        });
        var behavior = Behavior(brain);
        var definition = new BehaviorDefinition("decision", [
            new("source", "source", "{}", Output: new("Observation", InputSchema), SharedNeuron: Source),
            new("decision", "decision", """{"instructions":"Return an observation."}""", new("Observation", InputSchema), new("Decision", InputSchema))],
            [new("source", "decision")]);
        var running = await SaveAndStart(behavior, definition);
        var id = running.Bindings.Single(binding => binding.Role == "decision").Neuron;
        var processor = brain.Grains.GetGrain<IBehaviorNode>(id.ToGrainId());
        var neuron = brain.Grains.GetGrain<INeuron>(id.ToGrainId());
        var source = brain.Grains.GetGrain<INeuron>(Source.ToGrainId());
        await source.Fire(Signal.Create("Observation", """{"label":"first","value":1}"""), null, null, TestContext.Current.CancellationToken);
        await ReactionWait.UntilAsync(async () => (await processor.Status()).Error is not null, TestContext.Current.CancellationToken);
        Assert.Empty((await neuron.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        await source.Fire(Signal.Create("Observation", """{"label":"second","value":2}"""), null, null, TestContext.Current.CancellationToken);
        await ReactionWait.UntilAsync(async () => await neuron.ReadPendingCount() > 0, TestContext.Current.CancellationToken);
        Assert.Single(model.Calls);
        Assert.Empty((await neuron.ReadJournal(JournalKind.Outgoing, 0)).Delta);
    }

    [Fact]
    public async Task Duplicate_action_input_after_restart_retains_one_target_command_and_one_chart_point()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        NeuronId actionId;
        var delivery = SignalDelivery.Create(Signal.Create("Append", """{"point":{"label":"once","value":4},"title":"Observations"}"""), Source, 1, TimeProvider.System);
        await using (var brain = await StartBrain(directory))
        {
            var contract = new PayloadContract("Append", ActionSchema(brain));
            var definition = new BehaviorDefinition("action", [
                new("source", "source", "{}", Output: contract, SharedNeuron: Source),
                new("action", "action", ActionConfiguration, contract)], [new("source", "action")]);
            var running = await SaveAndStart(Behavior(brain), definition);
            actionId = running.Bindings.Single(binding => binding.Role == "action").Neuron;
            var action = brain.Grains.GetGrain<INeuron>(actionId.ToGrainId());
            Assert.Equal(DeliveryAdmission.Accepted, await action.Deliver(delivery, TestContext.Current.CancellationToken));
            var chart = brain.Grains.GetGrain<IChart>(Chart.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await chart.Read()).Points.Count == 1 && await action.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        }

        await using (var brain = await StartBrain(directory))
        {
            var action = brain.Grains.GetGrain<INeuron>(actionId.ToGrainId());
            Assert.Equal(DeliveryAdmission.Duplicate, await action.Deliver(delivery, TestContext.Current.CancellationToken));
            var chart = brain.Grains.GetGrain<IChart>(Chart.ToGrainId());
            Assert.Single((await chart.Read()).Points);
            var commands = await chart.ReadCommands(0);
            Assert.Single(commands.Delta.Select(command => command.Id).Distinct());
            Assert.Null((await brain.Grains.GetGrain<IBehaviorNode>(actionId.ToGrainId()).Status()).Error);
        }
    }

    private const string ActionConfiguration = """{"target":"chart:behavior-output","interface":"ui.chart","method":"append"}""";

    private static Task<BrainSimulation> StartBrain(string? directory = null) => BrainSimulation.StartAsync(new()
    {
        Modules = new([typeof(UIModule)]), PersistenceDirectory = directory
    });

    private static IBehavior Behavior(BrainSimulation brain) => brain.Grains.GetGrain<IBehavior>(new NeuronId("behavior", "fixture").ToGrainId());

    private static string ActionSchema(BrainSimulation brain)
    {
        var invoker = brain.SiloServices.GetRequiredService<INeuronInvoker>();
        var descriptor = invoker.Describe(Chart).Single(method => method.InterfaceAlias == "ui.chart" && method.MethodAlias == "append");
        var schema = JsonNode.Parse(descriptor.ArgsSchema!.Value.GetRawText())!.AsObject();
        var identity = invoker.ArgumentContractOf("ui.chart", "append")!.CommandIdPropertyName;
        schema["properties"]!.AsObject().Remove(identity!);
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

    private static BehaviorDefinition ChartPipeline(BrainSimulation brain) => new("filtered chart", [
        new("source", "source", "{}", Output: new("Observation", InputSchema), SharedNeuron: Source),
        new("filter", "filter", """{"path":"value","operator":"greaterThan","value":0}""", new("Observation", InputSchema), new("Filtered", InputSchema)),
        new("map", "map", """{"template":{"point":{"label":"$input.label","value":"$input.value","eventId":"$signalId"},"title":"Observations"}}""", new("Filtered", InputSchema), new("Append", ActionSchema(brain))),
        new("action", "action", ActionConfiguration, new("Append", ActionSchema(brain)))],
        [new("source", "filter"), new("filter", "map"), new("map", "action")]);

    private static async Task<BehaviorSnapshot> SaveAndStart(IBehavior behavior, BehaviorDefinition definition)
    {
        var validation = await behavior.Validate(definition);
        Assert.True(validation.Valid, string.Join("; ", validation.Errors));
        await behavior.Save(new(definition, CommandId.New()));
        await WaitStatus(behavior, BehaviorStatus.Stopped);
        await behavior.Start(new(CommandId.New()));
        return await WaitStatus(behavior, BehaviorStatus.Running);
    }

    private static async Task<BehaviorSnapshot> WaitStatus(IBehavior behavior, BehaviorStatus status)
    {
        BehaviorSnapshot? state = null;
        await ReactionWait.UntilAsync(async () =>
        {
            state = await behavior.Read();
            Assert.Null(state.Error);
            return state.Status == status;
        }, TestContext.Current.CancellationToken);
        return state!;
    }
}
