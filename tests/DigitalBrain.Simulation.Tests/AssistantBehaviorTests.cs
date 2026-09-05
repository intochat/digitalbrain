using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Scripting.Startup;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class AssistantBehaviorTests
{
    [Fact]
    public async Task ChatCanAdmitRunInspectAndRemoveThePersonalReviewScript()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var cancellationToken = deadline.Token;
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");
        var chatName = PrincipalPartition.InstanceName(actor.PrincipalId, "main");
        var source = (await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "examples", "personal-code-review.csx"), cancellationToken))
            .Replace("__CHAT_INSTANCE__", chatName, StringComparison.Ordinal);
        var client = new BehaviorChatClient(source);
        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest(
            [
                typeof(DigitalBrain.Execution.ExecutionModule),
                typeof(DigitalBrain.UI.UIModule),
                typeof(AIModule),
            ]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(client),
        });
        using var worker = new BehaviorScriptWorker(
            new DigitalBrainBehaviorAdmissionSource(sim.Brain, sim.Grains),
            new CSharpStartupScriptRunner(), sim.Brain, NullLogger<BehaviorScriptWorker>.Instance);
        await worker.StartAsync(cancellationToken);
        try
        {
            var chat = sim.Brain.Get<IChat>(chatName);
            var query = sim.Grains.GetGrain<IBehaviorsKernel>(sim.Brain.Get<IBehaviors>().Id.ToGrainId());
            await SendAsync("save personal review");
            await client.ReviewerStarted.Task.WaitAsync(cancellationToken);
            // A slow background review must not keep the owner root from accepting another message.
            await chat.RequestAsync(new SendMessage(CommandId.New(), "hello while reviewing", actor), cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            client.ContinueReview.TrySetResult();
            BehaviorDefinition? saved;
            do
            {
                saved = (await query.ReadCurrent()).SingleOrDefault();
                if (saved?.Status is BehaviorStatus.Completed or BehaviorStatus.Failed)
                {
                    break;
                }
                await Task.Delay(25, cancellationToken);
            } while (true);

            Assert.NotNull(saved);
            Assert.True(saved.Status == BehaviorStatus.Completed, $"{saved.Summary}: {string.Join(';', saved.Diagnostics)}");
            Assert.Equal(source, saved.Source);
            Assert.Equal(actor.PrincipalId, saved.Principal);
            Assert.Equal(PrincipalPartition.InstanceName(actor.PrincipalId, "personal-review"), saved.Name);
            Assert.Contains("read_repository_diff", client.ReviewerRequest);
            var transcript = await chat.RequestAsync(new ReadTranscriptRequest(chatName), cancellationToken);
            Assert.Contains(transcript.Transcript.Turns, turn => turn.Text == BehaviorChatClient.Review);

            var assistantSynapses = await sim.Brain.Get<IAssistant>("assistant").GetSynapsesAsync(cancellationToken);
            Assert.Contains(assistantSynapses, synapse => synapse.Target == sim.Brain.Get<IBehaviors>().Id
                && synapse.SignalType == nameof(AdmitBehavior));

            await SendAsync("inspect personal review");
            Assert.Contains(client.Results, result => result.Contains("Personal review posted to chat.", StringComparison.Ordinal));

            // Another authenticated principal cannot discover or remove this user's source.
            var other = new ActorContext(new PrincipalId(Guid.NewGuid()), "other");
            var otherChatName = PrincipalPartition.InstanceName(other.PrincipalId, "main");
            await SendAsync("remove personal review", other, otherChatName);
            Assert.Single(await query.ReadCurrent());

            await SendAsync("remove personal review");
            Assert.Empty(await query.ReadCurrent());
            Assert.DoesNotContain(client.SystemInstructions, text => text.Contains("list_experiences", StringComparison.Ordinal));

            async Task SendAsync(string text, ActorContext? sender = null, string? targetName = null)
            {
                var target = sim.Brain.Get<IChat>(targetName ?? chatName);
                var accepted = await target.RequestAsync(new SendMessage(CommandId.New(), text, sender ?? actor), cancellationToken);
                var terminal = await JournalWait.ForAsync(target, JournalKind.Outgoing,
                    delivery => delivery.Signal is TurnLifecycle life && life.TurnId == accepted.TurnId
                        && life.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled,
                    TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);
                var lifecycle = Assert.IsType<TurnLifecycle>(terminal.Signal);
                Assert.True(lifecycle.Status == ChatTurnStatus.Completed, lifecycle.Detail);
            }
        }
        finally
        {
            client.ContinueReview.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class BehaviorChatClient(string source) : IChatClient
    {
        internal const string Review = "Review complete: no concrete bugs found in the test diff.";
        public string ReviewerRequest { get; private set; } = "";
        public TaskCompletionSource ReviewerStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueReview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Results { get; } = new();
        public ConcurrentQueue<string> SystemInstructions { get; } = new();

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var history = messages.ToArray();
            foreach (var system in history.Where(message => message.Role == ChatRole.System))
            {
                SystemInstructions.Enqueue(system.Text);
            }
            var request = history.Last(message => message.Role == ChatRole.User).Text;
            var toolName = request switch
            {
                "save personal review" => "admit_behavior",
                "inspect personal review" => "read_behavior",
                "remove personal review" => "remove_behavior",
                _ => null,
            };
            if (toolName is null)
            {
                if (request.Contains("read_repository_diff", StringComparison.Ordinal))
                {
                    ReviewerRequest = request;
                    ReviewerStarted.TrySetResult();
                    await ContinueReview.Task.WaitAsync(cancellationToken);
                }
                yield return new ChatResponseUpdate(ChatRole.Assistant, Review) { FinishReason = ChatFinishReason.Stop };
                yield break;
            }

            var tool = Assert.Single(options!.Tools!.OfType<AIFunction>(), tool => tool.Name == toolName);
            var arguments = new AIFunctionArguments { ["name"] = "personal-review" };
            if (toolName == "admit_behavior")
            {
                arguments["source"] = source;
            }
            var result = await tool.InvokeAsync(arguments, cancellationToken);
            Results.Enqueue(result?.ToString() ?? "");
            yield return new ChatResponseUpdate(ChatRole.Assistant, result?.ToString()) { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
