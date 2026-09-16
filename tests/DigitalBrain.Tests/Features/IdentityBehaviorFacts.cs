using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IdentityBehaviorFacts
{
    private static readonly NeuronId Source = NeuronId.Plain("identity-source");
    private static readonly NeuronId Chart = new("chart", "identity-output");
    private static readonly NeuronId BehaviorId = new("behavior", "identity-behavior");

    [Fact]
    public async Task Revoked_grant_survives_cold_restart_and_pauses_new_dispatch_without_an_uncertain_effect()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        NeuronId actionId;
        await using (var brain = await Start(directory))
        {
            var identities = new IdentityService(brain.Grains);
            var owner = await identities.BootstrapOwnerAsync(new("owner", "owner"), "home", TimeSpan.FromHours(1));
            var grant = await identities.GrantAutomationAsync(owner.Token, "home", BehaviorId.ToString(), [Chart.ToString()], ["ui.chart.append"]);
            var running = await SaveAndStart(brain, Definition(brain, grant));
            actionId = running.Bindings.Single(binding => binding.Role == "action").Neuron;
            await Fire(brain, "before");
            var chart = brain.Grains.GetGrain<IChart>(Chart.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await chart.Read()).Points.Count == 1, TestContext.Current.CancellationToken);
            await identities.RevokeAutomationAsync(owner.Token, grant);
        }

        await using (var brain = await Start(directory))
        {
            var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
            Assert.Equal(BehaviorStatus.Running, (await behavior.Read()).Status);
            await Fire(brain, "denied");
            var action = brain.Grains.GetGrain<IBehaviorNode>(actionId.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await action.Status()).Error is not null, TestContext.Current.CancellationToken);
            var status = await action.Status();
            Assert.Null(status.UncertainAction);
            var chart = brain.Grains.GetGrain<IChart>(Chart.ToGrainId());
            Assert.Equal("before", Assert.Single((await chart.Read()).Points).Label);
            Assert.Single((await chart.ReadCommands(0)).Delta.Select(command => command.Id).Distinct());
        }
    }

    [Theory]
    [InlineData("chart:somebody-else", "ui.chart.append")]
    [InlineData("chart:identity-output", "ui.chart.clear")]
    public async Task Grant_must_cover_the_exact_target_and_action_before_any_side_effect(string allowedTarget, string allowedAction)
    {
        await using var brain = await Start();
        var identities = new IdentityService(brain.Grains);
        var owner = await identities.BootstrapOwnerAsync(new("owner", "owner"), "home", TimeSpan.FromHours(1));
        var grant = await identities.GrantAutomationAsync(owner.Token, "home", BehaviorId.ToString(), [allowedTarget], [allowedAction]);
        var running = await SaveAndStart(brain, Definition(brain, grant));
        await Fire(brain, "denied");
        var actionId = running.Bindings.Single(binding => binding.Role == "action").Neuron;
        var action = brain.Grains.GetGrain<IBehaviorNode>(actionId.ToGrainId());
        await ReactionWait.UntilAsync(async () => (await action.Status()).Error is not null, TestContext.Current.CancellationToken);
        Assert.Null((await action.Status()).UncertainAction);
        var chart = brain.Grains.GetGrain<IChart>(Chart.ToGrainId());
        Assert.Empty((await chart.Read()).Points);
        Assert.Empty((await chart.ReadCommands(0)).Delta);
    }

    [Fact]
    public async Task Authenticated_authoring_requires_the_callers_active_grant_for_this_behavior()
    {
        await using var brain = await Start();
        var identities = new IdentityService(brain.Grains);
        var owner = await identities.BootstrapOwnerAsync(new("owner", "owner"), "home", TimeSpan.FromHours(1));
        var matching = await identities.GrantAutomationAsync(owner.Token, "home", BehaviorId.ToString(), [Chart.ToString()], ["ui.chart.append"]);
        var anotherBehavior = await identities.GrantAutomationAsync(owner.Token, "home", "behavior:elsewhere", [Chart.ToString()], ["ui.chart.append"]);
        var member = await identities.CreateMemberAsync(owner.Token, new("test", "member"), "home");
        await identities.SetMembershipAsync(owner.Token, "home", member.UserId, WorkspaceRole.Owner);
        var memberSession = Assert.IsType<IdentitySession>(await identities.SignInAsync(new("test", "member"), TimeSpan.FromHours(1)));
        var anotherUser = await identities.GrantAutomationAsync(memberSession.Token, "home", BehaviorId.ToString(), [Chart.ToString()], ["ui.chart.append"]);
        var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
        RequestContext.Set("DigitalBrain.Identity.Caller", new IdentityCaller(owner.UserId));
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => behavior.Save(new(Definition(brain, matching) with { Authorization = null }, CommandId.New())));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => behavior.Save(new(Definition(brain, anotherBehavior), CommandId.New())));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => behavior.Save(new(Definition(brain, anotherUser), CommandId.New())));
            await behavior.Save(new(Definition(brain, matching), CommandId.New()));
            await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Stopped, TestContext.Current.CancellationToken);
            await identities.RevokeAutomationAsync(owner.Token, matching);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => behavior.Start(new(CommandId.New())));
            Assert.Equal(BehaviorStatus.Stopped, (await behavior.Read()).Status);
        }
        finally
        {
            RequestContext.Remove("DigitalBrain.Identity.Caller");
        }
    }

    private static Task<BrainSimulation> Start(string? directory = null) => BrainSimulation.StartAsync(new()
    {
        Modules = new([typeof(UIModule)]),
        PersistenceDirectory = directory
    });

    private static async Task Fire(BrainSimulation brain, string label)
    {
        var source = brain.Grains.GetGrain<INeuron>(Source.ToGrainId());
        await source.Fire(Signal.Create("Append", $$"""{"point":{"label":"{{label}}","value":1},"title":"Identity"}"""), null, null, TestContext.Current.CancellationToken);
    }

    private static BehaviorDefinition Definition(BrainSimulation brain, string grantId)
    {
        var invoker = brain.SiloServices.GetRequiredService<INeuronInvoker>();
        var descriptor = invoker.Describe(Chart).Single(method => method.InterfaceAlias == "ui.chart" && method.MethodAlias == "append");
        var schema = JsonNode.Parse(descriptor.ArgsSchema!.Value.GetRawText())!.AsObject();
        var identity = invoker.ArgumentContractOf("ui.chart", "append")!.CommandIdPropertyName!;
        schema["properties"]!.AsObject().Remove(identity);
        var required = schema["required"]!.AsArray();
        for (var index = required.Count - 1; index >= 0; index--)
        {
            if (required[index]!.GetValue<string>() == identity)
            {
                required.RemoveAt(index);
            }
        }
        var contract = new PayloadContract("Append", schema.ToJsonString());
        return new("authorized chart", [
            new("source", "source", "{}", Output: contract, SharedNeuron: Source),
            new("action", "action", """{"target":"chart:identity-output","interface":"ui.chart","method":"append"}""", contract)],
            [new("source", "action")], new("home", grantId));
    }

    private static async Task<BehaviorSnapshot> SaveAndStart(BrainSimulation brain, BehaviorDefinition definition)
    {
        var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
        await behavior.Save(new(definition, CommandId.New()));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Stopped, TestContext.Current.CancellationToken);
        await behavior.Start(new(CommandId.New()));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Running, TestContext.Current.CancellationToken);
        return await behavior.Read();
    }
}
