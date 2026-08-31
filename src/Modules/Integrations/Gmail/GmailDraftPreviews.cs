using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.Core;
using DigitalBrain.Integrations.Mcp;

namespace DigitalBrain.Integrations.Gmail;

internal sealed partial class GmailDraftPreviews(GmailConnections connections, GmailPendingActions actions,
    GmailMcpSessions sessions, IUntrustedContentScreen screen) : ITrustedUserCommandHandler
{
    private readonly Dictionary<string, Preview> _previews = new(StringComparer.Ordinal);
    internal void Prune()
    {
        lock (_previews)
        {
            foreach (var key in _previews.Where(p => p.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray())
            {
                _previews.Remove(key);
            }
        }
    }
    internal async Task<string> CreateAsync(OwnerId owner, string[] to, string[]? cc, string[]? bcc,
        string subject, string body, CancellationToken cancellationToken)
    {
        var context = RequireContext(owner);
        if (context.AllowedToolNames is not null)
        {
            throw new GmailOperationException("Login does not approve a draft. Ask for a fresh preview in a new turn.");
        }

        var identity = connections.Identity(owner);
        if (!identity.CanCompose) { throw new GmailAuthenticationRequiredException(); }
        var payload = new Draft([.. to], cc is null ? [] : [.. cc], bcc is null ? [] : [.. bcc], subject, body);
        var args = payload.Arguments();
        GmailContent.ValidateArguments("create_draft", args);
        await screen.ScreenAsync(JsonSerializer.Serialize(args), cancellationToken).ConfigureAwait(false);
        lock (_previews)
        {
            foreach (var expiredId in _previews.Where(p => p.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray())
            {
                _previews.Remove(expiredId);
            }

            var existing = _previews.Values.FirstOrDefault(p => SameTurn(p.Context, context));
            if (existing is not null)
            {
                return existing.Response;
            }

            if (_previews.Count >= 128)
            {
                throw new GmailOperationException("Too many draft previews. Wait for a preview to expire.");
            }

            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var preview = new Preview(id, context, identity, payload);
            if (System.Text.Encoding.UTF8.GetByteCount(preview.Response) > 32768)
            {
                throw new GmailOperationException("The complete draft preview exceeds 32 KiB. Shorten its contents.");
            }
            _previews.Add(id, preview);
            return preview.Response;
        }
    }
    public string? ResponseFor(AgentTurnContext context)
    {
        lock (_previews)
        {
            return _previews.Values.FirstOrDefault(p => SameTurn(p.Context, context) && !p.Consumed)?.Response;
        }
    }
    public void ResponsePublished(AgentTurnContext context, string response)
    {
        lock (_previews)
        {
            foreach (var p in _previews.Values.Where(p => SameTurn(p.Context, context) && p.Response == response && !p.Consumed))
            {
                p.Published = true;
            }
        }
    }
    public async Task<string?> HandleAsync(string originalUserText, CancellationToken cancellationToken)
    {
        var match = Confirmation().Match(originalUserText);
        if (!match.Success)
        {
            return null;
        }

        var context = AgentTurnContext.Current;
        if (context is null || context.AllowedToolNames is not null || VerifiedActor.Current != context.Actor)
        {
            return "Draft confirmation must be a new authenticated user message after reviewing a preview.";
        }

        Preview preview;
        lock (_previews)
        {
            if (!_previews.TryGetValue(match.Groups[1].Value, out preview!) || !preview.Published
                || preview.Context.Chat != context.Chat || preview.Context.Actor != context.Actor
                || preview.Context.CommandId == context.CommandId || preview.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return "This draft preview is unavailable or expired. Request a fresh preview and review it before confirming.";
            }

            if (preview.Consumed)
            {
                return preview.Result;
            }

            try
            {
                if (connections.Identity(context.Chat.Owner) != preview.Identity)
                {
                    return "The connected Google account changed. Request and confirm a fresh draft preview.";
                }
            }
            catch (GmailAuthenticationRequiredException)
            {
                actions.RequireLogin(compose: true, cancellationToken);
                return "Reconnect Gmail, then request and confirm a fresh preview. Nothing was created.";
            }
            if (!preview.Identity.CanCompose)
            {
                actions.RequireLogin(compose: true, cancellationToken);
                return "Gmail compose access is required. Login cannot create a draft; request a fresh preview afterward.";
            }
            // At-most-once boundary, BEFORE any network operation. The result remains a tombstone
            // through expiry even on cancellation, process-level uncertainty or repeated commands.
            preview.Consumed = true;
            preview.ConfirmationCommand = context.CommandId;
        }
        try
        {
            var result = await sessions.CallAsync(context.Chat.Owner,
                new McpIntegrationEndpoint("gmail", new Uri(McpIntegrationEndpoint.GmailUri)), "create_draft",
                preview.Payload.Arguments(), cancellationToken, preview.Identity).ConfigureAwait(false);
            lock (_previews)
            {
                preview.Result = "Gmail draft created (not sent). " + result.GetRawText();
            }
        }
        catch (Exception)
        {
            // No automatic consent/resume/retry even if a response, connection or screening failed.
            // Do not claim failure proves no mutation: the server may have committed already.
            lock (_previews)
            {
                preview.Result = "The Gmail draft submission outcome could not be confirmed. This preview will never be retried. Check Gmail Drafts before requesting a new preview.";
            }
        }
        return preview.Result;
    }
    internal static AgentTurnContext RequireContext(OwnerId owner)
    {
        var context = AgentTurnContext.Current;
        if (context is null || context.Chat.Owner != owner || VerifiedActor.Current != context.Actor)
        {
            throw new GmailOperationException("Gmail tools require the current authenticated owner's chat.");
        }

        return context;
    }
    private static bool SameTurn(AgentTurnContext a, AgentTurnContext b)
        => a.Chat == b.Chat && a.Actor == b.Actor && a.CommandId == b.CommandId;
    [GeneratedRegex(@"\Aconfirm gmail draft ([a-f0-9]{64})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Confirmation();
    private sealed class Draft(string[] to, string[] cc, string[] bcc, string subject, string body)
    {
        internal Dictionary<string, object?> Arguments() => new() { ["to"] = to, ["cc"] = cc, ["bcc"] = bcc, ["subject"] = subject, ["body"] = body };
        internal string Display
        {
            get
            {
                var content = $"To: {string.Join(", ", to)}\nCc: {string.Join(", ", cc)}\nBcc: {string.Join(", ", bcc)}\nSubject: {subject}\n\nBody (plain text):\n{body}";
                var longest = 0; var run = 0;
                foreach (var c in content)
                {
                    run = c == '`' ? run + 1 : 0;
                    longest = Math.Max(longest, run);
                }
                var fence = new string('`', Math.Max(3, longest + 1));
                return $"{fence}text\n{content}\n{fence}";
            }
        }
    }
    private sealed class Preview(string id, AgentTurnContext context, GmailIdentity identity, Draft payload)
    {
        internal readonly AgentTurnContext Context = context;
        internal readonly GmailIdentity Identity = identity;
        internal readonly Draft Payload = payload;
        internal readonly DateTimeOffset ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        internal string Response => $"Gmail draft preview — nothing has been created or sent.\n\n{Payload.Display}\n\nPreview expires: {ExpiresAt:O}\nTo create exactly this draft, send this exact message:\nconfirm gmail draft {id}";
        internal bool Published;
        internal bool Consumed;
        internal CommandId? ConfirmationCommand;
        internal string Result = "This Gmail preview was already submitted. Check Gmail Drafts; it will not be retried.";
    }
}
