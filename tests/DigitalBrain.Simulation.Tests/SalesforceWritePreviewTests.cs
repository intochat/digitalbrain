using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Salesforce;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SalesforceWritePreviewTests
{
    [Fact]
    public async Task Signal_delivery_username_does_not_change_the_verified_principal()
    {
        var fixture = new Fixture();
        using var verified = VerifiedActor.Enter(new ActorContext(fixture.Origin.Actor.PrincipalId, "_delivery"));
        string response;
        using (AgentTurnContext.Enter(fixture.Origin))
        {
            response = await fixture.Create();
            fixture.Previews.ResponsePublished(fixture.Origin, response);
        }
        using (AgentTurnContext.Enter(fixture.Origin with { CommandId = CommandId.New() }))
        {
            await fixture.Previews.HandleAsync(Confirm(response), Token);
        }
        Assert.Equal(1, fixture.Writes);
    }

    [Fact]
    public async Task Exact_published_arguments_are_immutable_and_confirmation_submits_once()
    {
        var fixture = new Fixture();
        string response;
        var mutableBody = new Dictionary<string, object?> { ["Name"] = "Reviewed name" };
        using (fixture.Enter(fixture.Origin))
        {
            response = await fixture.Previews.CreateAsync(fixture.Identity, fixture.Tool,
                new() { ["sobject-name"] = "Account", ["body"] = mutableBody }, Token);
            mutableBody["Name"] = "Changed after preview";
            Assert.Equal(response, fixture.Previews.ResponseFor(fixture.Origin));
            fixture.Previews.ResponsePublished(fixture.Origin, response);
            Assert.Equal(0, fixture.Writes);
        }
        using (fixture.Enter(fixture.Origin with { CommandId = CommandId.New() }))
        {
            var result = await fixture.Previews.HandleAsync(Confirm(response), Token);
            Assert.Contains("submitted", result, StringComparison.Ordinal);
            var duplicate = await fixture.Previews.HandleAsync(Confirm(response), Token);
            Assert.Equal(result, duplicate);
        }
        Assert.Equal(1, fixture.Writes);
        Assert.Equal("Reviewed name", fixture.Submitted!.Value.GetProperty("body").GetProperty("Name").GetString());
        Assert.Equal(fixture.Tool.JsonSchema.GetRawText(), fixture.SubmittedSchema);
    }

    [Theory]
    [InlineData("unpublished")]
    [InlineData("same-command")]
    [InlineData("other-actor")]
    [InlineData("other-chat")]
    [InlineData("expired")]
    [InlineData("altered-publication")]
    [InlineData("restricted-continuation")]
    public async Task Confirmation_rejects_untrusted_or_unreviewed_context(string reason)
    {
        var fixture = new Fixture();
        string response;
        using (fixture.Enter(fixture.Origin))
        {
            response = await fixture.Create();
            if (reason != "unpublished")
            {
                fixture.Previews.ResponsePublished(fixture.Origin, reason == "altered-publication" ? response + " changed" : response);
            }
        }
        var next = fixture.Origin with { CommandId = CommandId.New() };
        next = reason switch
        {
            "same-command" => fixture.Origin,
            "other-actor" => next with { Actor = new ActorContext(PrincipalId.New(), "other") },
            "other-chat" => next with { Chat = new NeuronId("chat", fixture.Origin.Chat.Owner, "another") },
            "restricted-continuation" => next with { AllowedToolNames = ["getUserInfo"] },
            _ => next,
        };
        if (reason == "expired") { fixture.Time.Advance(TimeSpan.FromMinutes(11)); }
        using (fixture.Enter(next))
        {
            await fixture.Previews.HandleAsync(Confirm(response), Token);
        }
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task Model_confirmation_flag_cannot_write_or_replace_the_original_proposal()
    {
        var fixture = new Fixture();
        using var turn = fixture.Enter(fixture.Origin);
        var first = await fixture.Create();
        var second = await fixture.Previews.CreateAsync(fixture.Identity, fixture.Tool,
            new() { ["body"] = new { Name = "replacement" }, ["confirmed"] = true }, Token);
        Assert.Equal(first, second);
        Assert.Equal(0, fixture.Writes);
        Assert.Null(await fixture.Previews.HandleAsync("confirmed=true", Token));
    }

    [Fact]
    public async Task Changed_binding_is_rejected_before_consumption_and_uncertain_writes_never_retry()
    {
        var fixture = new Fixture();
        string response;
        using (fixture.Enter(fixture.Origin))
        {
            response = await fixture.Create();
            fixture.Previews.ResponsePublished(fixture.Origin, response);
        }
        using (fixture.Enter(fixture.Origin with { CommandId = CommandId.New() }))
        {
            fixture.BindingChanged = true;
            var changed = await fixture.Previews.HandleAsync(Confirm(response), Token);
            Assert.Contains("binding changed", changed, StringComparison.Ordinal);
            Assert.Equal(0, fixture.Writes);
            fixture.BindingChanged = false;
            fixture.FailSubmission = true;
            var uncertain = await fixture.Previews.HandleAsync(Confirm(response), Token);
            Assert.Contains("could not be confirmed", uncertain, StringComparison.Ordinal);
            fixture.FailSubmission = false;
            Assert.Equal(uncertain, await fixture.Previews.HandleAsync(Confirm(response), Token));
            Assert.Equal(1, fixture.Writes);
        }
    }

    [Fact]
    public async Task Login_continuation_cannot_prepare_a_mutation()
    {
        var fixture = new Fixture();
        using var turn = fixture.Enter(fixture.Origin with { AllowedToolNames = ["getUserInfo"] });
        await Assert.ThrowsAsync<McpOperationException>(fixture.Create);
        Assert.Equal(0, fixture.Writes);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Confirm(string response) => Regex.Match(response, @"confirm salesforce change [a-f0-9]{64}").Value;

    private sealed class Fixture
    {
        internal readonly Clock Time = new();
        internal readonly SalesforceWritePreviews Previews;
        internal readonly AIFunction Tool = AIFunctionFactory.Create((string body) => body, "createRecord");
        internal readonly AgentTurnContext Origin;
        internal readonly SalesforceInvocation Identity;
        internal int Writes;
        internal bool BindingChanged;
        internal bool FailSubmission;
        internal JsonElement? Submitted;
        internal string? SubmittedSchema;

        internal Fixture()
        {
            var owner = new OwnerId("dev");
            var actor = new ActorContext(PrincipalId.New(), "salesforce-user");
            var target = NeuronId.For<ISalesforce>(owner, PrincipalPartition.InstanceName(actor.PrincipalId, "salesforce-local"));
            Origin = new AgentTurnContext(new NeuronId("chat", owner, PrincipalPartition.InstanceName(actor.PrincipalId, "main")), CommandId.New(), actor);
            Identity = new SalesforceInvocation(target, actor.PrincipalId, new SalesforceBinding(owner, actor.PrincipalId, "revision"));
            Previews = new SalesforceWritePreviews(_ =>
            {
                if (BindingChanged) { throw new McpOperationException("changed", McpFailureKind.ConnectionChanged); }
            }, (_, _, schema, _, arguments, _) =>
            {
                Writes++;
                Submitted = arguments.Clone();
                SubmittedSchema = schema;
                if (FailSubmission) { throw new HttpRequestException("response lost after server commit"); }
                return Task.FromResult<object?>(JsonSerializer.SerializeToElement(new { ok = true }));
            }, new Screen(), Time);
        }

        internal Task<string> Create() => Previews.CreateAsync(Identity, Tool, new() { ["body"] = "Reviewed" }, Token);
        internal IDisposable Enter(AgentTurnContext turn) => new Turn(turn);
    }

    private sealed class Turn : IDisposable
    {
        private readonly IDisposable _actor;
        private readonly IDisposable _turn;
        internal Turn(AgentTurnContext context) => (_actor, _turn) = (VerifiedActor.Enter(context.Actor), AgentTurnContext.Enter(context));
        public void Dispose() { _turn.Dispose(); _actor.Dispose(); }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class Screen : IUntrustedContentScreen
    {
        public Task ScreenAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
