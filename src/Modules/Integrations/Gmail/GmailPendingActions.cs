using System.Security.Cryptography;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Integrations.Gmail;

internal sealed class GmailPendingActions(GmailOAuthConfiguration configuration, IServiceProvider services) : IUserActionSource
{
    internal static readonly string[] ReadTools = ["gmail_get_current_account", "gmail_search_threads", "gmail_get_thread", "gmail_list_labels"];
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    internal UserActionRequest RequireLogin(bool compose, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        configuration.RequireConfigured();
        var context = AgentTurnContext.Current ?? throw new GmailOperationException("Request Gmail access from an authenticated chat.");
        if (VerifiedActor.Current != context.Actor)
        {
            throw new GmailOperationException("An authenticated chat actor is required.");
        }

        if (context.AllowedToolNames is not null)
        {
            throw new GmailOperationException("Gmail authorization did not complete this read. Send a new request after checking access; automatic login will not repeat.");
        }

        lock (_pending)
        {
            foreach (var key in _pending.Where(p => p.Value.Done && p.Value.Action.ExpiresAt <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray())
            {
                _pending.Remove(key);
            }

            var existing = _pending.Values.FirstOrDefault(p => p.Context == context);
            if (existing is not null)
            {
                if (existing.Done)
                {
                    throw new GmailOperationException("This Gmail login request is already settled. Send a new request.");
                }

                if (compose) { existing.Compose = true; existing.Action = existing.Action with { ResumeToolNames = [] }; }
                return existing.Action;
            }
            if (_pending.Count >= 128)
            {
                throw new GmailOperationException("Too many pending Gmail logins. Wait for an existing request to expire.");
            }

            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var action = new UserActionRequest(Guid.NewGuid().ToString("N"), "gmail", "Gmail",
                "Sign in with Google to connect Gmail. Credentials stay outside the conversation. Login never creates a draft.",
                new Uri(configuration.PublicOrigin, $"{GmailOAuthEndpoints.LoginPath}?request={id}").AbsoluteUri,
                DateTimeOffset.UtcNow.AddMinutes(10), compose ? [] : [.. ReadTools]);
            var pending = new Pending(context, action, compose);
            _pending.Add(id, pending);
            pending.Cancellation = cancellationToken.Register(() => Cancel(context));
            cancellationToken.ThrowIfCancellationRequested();
            return action;
        }
    }

    public UserActionRequest? Find(OwnerId owner, CommandId commandId)
    {
        var context = AgentTurnContext.Current;
        lock (_pending)
        {
            return _pending.Values.FirstOrDefault(p => !p.Done && p.Context.Chat.Owner == owner
            && p.Context.CommandId == commandId && context?.Chat == p.Context.Chat && context.Actor == p.Context.Actor)?.Action;
        }
    }
    public void Cancel(AgentTurnContext context)
    {
        lock (_pending)
        {
            foreach (var p in _pending.Values.Where(p => p.Context.Chat == context.Chat && p.Context.CommandId == context.CommandId && p.Context.Actor == context.Actor))
            { p.Done = true; p.Outcome = false; p.Cancellation.Unregister(); }
        }
    }
    internal bool TryBegin(string? request, out bool compose)
    {
        lock (_pending)
        {
            compose = false;
            if (request is null || !_pending.TryGetValue(request, out var p) || !Active(p) || p.State != 0)
            {
                return false;
            }

            p.State = 1; compose = p.Compose; return true;
        }
    }
    internal bool TryClaim(string? request)
    {
        lock (_pending)
        {
            if (request is null || !_pending.TryGetValue(request, out var p) || !Active(p) || p.State != 1)
            {
                return false;
            }

            p.State = 2; return true;
        }
    }
    internal async Task AcceptAsync(string request, Func<OwnerId, bool, Action<Action>, Task> accept)
    {
        Pending p;
        lock (_pending)
        {
            if (!_pending.TryGetValue(request, out p!) || !Active(p) || p.State != 2)
            {
                throw new GmailOperationException("This Gmail login expired or was cancelled.");
            }
        }
        await accept(p.Context.Chat.Owner, p.Compose, commit =>
        {
            lock (_pending)
            {
                if (!Active(p) || p.State != 2)
                {
                    throw new GmailOperationException("This Gmail login expired or was cancelled.");
                }
                commit();
                p.Outcome = true;
            }
        }).ConfigureAwait(false);
    }
    internal void Reject(string? request)
    {
        lock (_pending)
        {
            if (request is not null && _pending.TryGetValue(request, out var p) && !p.Done && p.Outcome is null)
            {
                p.Outcome = false;
            }
        }
    }
    internal async Task DeliverAsync(CancellationToken cancellationToken)
    {
        Pending[] ready;
        lock (_pending)
        {
            foreach (var p in _pending.Values.Where(p => !p.Done && p.Action.ExpiresAt <= DateTimeOffset.UtcNow))
            {
                p.Outcome = false;
            }

            ready = _pending.Values.Where(p => !p.Done && p.Outcome is not null).ToArray();
        }
        foreach (var p in ready)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var continuation = services.GetRequiredService<IUserActionContinuation>();
            if (p.Action.ExpiresAt > DateTimeOffset.UtcNow
                && !await continuation.IsWaitingAsync(p.Context, p.Action.Id, deadline.Token).ConfigureAwait(false))
            {
                // Callback can finish before the original AI turn publishes its login card.
                // Wait for that durable state instead of losing the one-shot continuation.
                continue;
            }
            await continuation.CompleteAsync(p.Context, p.Action.Id, p.Outcome == true, deadline.Token).ConfigureAwait(false);
            lock (_pending)
            {
                p.Done = true;
                p.Cancellation.Unregister();
            }
        }
    }
    private static bool Active(Pending p) => !p.Done && p.Outcome is null && p.Action.ExpiresAt > DateTimeOffset.UtcNow;
    private sealed class Pending(AgentTurnContext context, UserActionRequest action, bool compose)
    {
        internal readonly AgentTurnContext Context = context;
        internal UserActionRequest Action = action;
        internal bool Compose = compose;
        internal int State;
        internal bool? Outcome;
        internal bool Done;
        internal CancellationTokenRegistration Cancellation;
    }
}

internal sealed class GmailCompletionWorker(GmailPendingActions pending, GmailMcpSessions sessions, GmailDraftPreviews previews, ILogger<GmailCompletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                previews.Prune();
                await sessions.PruneAsync().ConfigureAwait(false);
                await pending.DeliverAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Gmail login completion delivery failed; the idempotent notification will be retried."); }
        }
    }
}
