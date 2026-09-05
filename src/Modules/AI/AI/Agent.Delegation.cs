using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;

namespace DigitalBrain.AI;

public abstract partial class Agent
{
    private sealed class TurnRequests(Agent source, CancellationToken turnCancellation) : IAgentRequests, IDisposable
    {
        private bool _active = true;

        public async Task<AgentReply> RequestAsync<TAgent>(
            string instanceName, AgentRequest request, CancellationToken cancellationToken = default)
            where TAgent : IAgent
        {
            if (!_active)
            {
                throw new InvalidOperationException("This agent request capability has expired with its model turn.");
            }

            ArgumentNullException.ThrowIfNull(request);
            if (VerifiedActor.Current is not { } actor || !PrincipalPartition.OwnsInstance(actor.PrincipalId, instanceName))
            {
                throw new NeuronAuthorizationException("The delegated agent must belong to the current user.");
            }

            // Login continuations authorize named direct reads. They do not grant a
            // new specialist's entire toolset; preserve that boundary explicitly.
            if (AgentTurnContext.Current?.AllowedToolNames is not null)
            {
                throw new InvalidOperationException("Specialist delegation requires an ordinary user turn, not a restricted login continuation.");
            }

            var target = NeuronId.For<TAgent>(source.Id.Owner, instanceName);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turnCancellation, cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            deadline.Token.ThrowIfCancellationRequested();
            var operation = Guid.NewGuid();
            var started = Stopwatch.GetTimestamp();
            await source.RecordOutgoingAsync(new AgentActivity(operation, "delegation", "started", target.Type, target))
                .ConfigureAwait(true);
            var state = "failed";
            try
            {
                var reply = await source.RequestAsync(target, request, deadline.Token).ConfigureAwait(true);
                state = "completed";
                return reply;
            }
            catch (OperationCanceledException)
            {
                state = "cancelled";
                throw;
            }
            finally
            {
                await source.RecordOutgoingAsync(new AgentActivity(operation, "delegation", state, target.Type, target,
                    DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds)).ConfigureAwait(true);
            }
        }

        public void Dispose() => _active = false;
    }
}
