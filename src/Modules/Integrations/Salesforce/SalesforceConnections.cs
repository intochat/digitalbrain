using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Integrations.Salesforce;

// The kernel owns one connection per owner. Tokens and OAuth transactions are intentionally
// volatile: restarting the kernel disconnects Salesforce, without persisting credentials in Orleans.
internal sealed class SalesforceConnections(
    SalesforceOAuthConfiguration configuration,
    IServiceProvider services) : IUserActionSource, IDisposable
{
    private readonly ConcurrentDictionary<OwnerId, CredentialSlot> _credentials = new();
    private readonly ConcurrentDictionary<string, PendingLogin> _pending = new(StringComparer.Ordinal);
    private readonly HttpClient _oauthHttp = new(new HttpClientHandler { AllowAutoRedirect = false });

    internal OwnerId CurrentOwner => AgentTurnContext.Current?.Chat.Owner ?? configuration.Owner;

    public UserActionRequest? Find(OwnerId owner, CommandId commandId)
    {
        var context = AgentTurnContext.Current;
        return context is null ? null : _pending.Values.FirstOrDefault(p => p.Context.Chat.Owner == owner
            && p.Context.Chat == context.Chat && p.Context.Actor == context.Actor
            && p.Context.CommandId == commandId && p.Status < 3 && p.Action.ExpiresAt > DateTimeOffset.UtcNow)?.Action;
    }

    internal UserActionRequest RequireLogin(bool readOnly)
    {
        var context = AgentTurnContext.Current
            ?? throw new SalesforceAuthenticationRequiredException();
        lock (_pending)
        {
            foreach (var pair in _pending.Where(p => p.Value.Action.ExpiresAt <= DateTimeOffset.UtcNow).ToArray())
            {
                _pending.TryRemove(pair.Key, out _);
            }
            var existing = _pending.Values.FirstOrDefault(p => p.Context.Chat == context.Chat
                && p.Context.Actor == context.Actor && p.Context.CommandId == context.CommandId && p.Status < 3);
            if (existing is not null)
            {
                if (!readOnly)
                {
                    existing.Action = existing.Action with { ResumeToolNames = [] };
                }
                return existing.Action;
            }
            if (_pending.Count >= 128)
            {
                throw new InvalidOperationException("Too many pending Salesforce logins. Cancel an existing request or wait for it to expire.");
            }
            var request = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var action = new UserActionRequest(
                Guid.NewGuid().ToString("N"), "salesforce", "Salesforce",
                "Log in to Salesforce to continue this request. Your credentials stay outside the conversation.",
                new Uri(configuration.PublicOrigin, $"{SalesforceOAuthEndpoints.LoginPath}?request={request}").AbsoluteUri,
                DateTimeOffset.UtcNow.AddMinutes(10),
                readOnly ? ["salesforce_get_current_user", "salesforce_soql_query"] : []);
            _pending[request] = new PendingLogin(context, action);
            return action;
        }
    }

    internal bool TryBegin(string? request)
        => request is not null && _pending.TryGetValue(request, out var pending)
            && pending.Action.ExpiresAt > DateTimeOffset.UtcNow
            && Interlocked.CompareExchange(ref pending.Status, 1, 0) == 0;

    internal bool TryClaimCallback(string? request)
        => request is not null && _pending.TryGetValue(request, out var pending)
            && pending.Action.ExpiresAt > DateTimeOffset.UtcNow
            && Interlocked.CompareExchange(ref pending.Status, 2, 1) == 1;

    internal async Task AcceptTokensAsync(
        string request, string? accessToken, string? refreshToken, TimeSpan? expiresIn,
        CancellationToken cancellationToken)
    {
        if (!_pending.TryGetValue(request, out var pending)
            || pending.Action.ExpiresAt <= DateTimeOffset.UtcNow
            || pending.Status != 2 || pending.CompletionRequested != 0)
        {
            throw new InvalidOperationException("The Salesforce login request expired or was already used.");
        }
        ValidateToken(accessToken);
        var slot = _credentials.GetOrAdd(pending.Context.Chat.Owner, static _ => new CredentialSlot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            slot.AccessToken = accessToken;
            slot.RefreshToken = refreshToken;
            slot.ExpiresAt = Expiry(expiresIn);
            Interlocked.Exchange(ref pending.CompletionRequested, 1);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    internal void RejectLogin(string? verifiedRequest)
    {
        if (verifiedRequest is not null && _pending.TryGetValue(verifiedRequest, out var pending)
            && pending.Status == 2)
        {
            // A validated callback outcome is immutable. A duplicate cannot cancel success.
            Interlocked.CompareExchange(ref pending.CompletionRequested, 2, 0);
        }
    }

    private async Task FinishAsync(PendingLogin pending, CancellationToken cancellationToken)
    {
        if (pending.Status >= 3)
        {
            return;
        }
        if (Interlocked.Exchange(ref pending.Finishing, 1) != 0)
        {
            return;
        }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await services.GetRequiredService<IUserActionContinuation>()
                .CompleteAsync(pending.Context, pending.Action.Id, pending.CompletionRequested == 1, deadline.Token)
                .ConfigureAwait(false);
            Interlocked.Exchange(ref pending.Status, 3);
        }
        finally
        {
            // If delivery fails, the completion worker retries the same idempotent command.
            Interlocked.Exchange(ref pending.Finishing, 0);
        }
    }

    internal async Task<int> DeliverPendingCompletionsAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        foreach (var pair in _pending.Where(p => p.Value.CompletionRequested != 0 && p.Value.Status < 3))
        {
            try
            {
                await FinishAsync(pair.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failures++;
            }
        }
        return failures;
    }

    internal async Task<string> GetAccessTokenAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        var slot = _credentials.GetOrAdd(owner, static _ => new CredentialSlot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.AccessToken is not null && slot.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return slot.AccessToken;
            }
            if (slot.RefreshToken is null)
            {
                throw new SalesforceAuthenticationRequiredException();
            }
            using var request = configuration.RefreshRequest(slot.RefreshToken);
            using var response = await _oauthHttp.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException("Salesforce token refresh is temporarily unavailable. Try again shortly.", null, response.StatusCode);
                }
                slot.AccessToken = null;
                slot.RefreshToken = null;
                throw new SalesforceAuthenticationRequiredException();
            }
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var token = root.GetProperty("access_token").GetString();
            ValidateToken(token);
            slot.AccessToken = token;
            if (root.TryGetProperty("refresh_token", out var refresh))
            {
                slot.RefreshToken = refresh.GetString();
            }
            slot.ExpiresAt = root.TryGetProperty("expires_in", out var expires)
                && double.TryParse(expires.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                    ? Expiry(TimeSpan.FromSeconds(seconds)) : DateTimeOffset.MaxValue;
            return token!;
        }
        catch (JsonException)
        {
            slot.AccessToken = null;
            slot.RefreshToken = null;
            throw new SalesforceAuthenticationRequiredException();
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    internal async Task RejectTokenAsync(OwnerId owner, string token, CancellationToken cancellationToken)
    {
        if (!_credentials.TryGetValue(owner, out var slot))
        {
            return;
        }
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(slot.AccessToken, token, StringComparison.Ordinal))
            {
                slot.AccessToken = null;
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private static DateTimeOffset Expiry(TimeSpan? lifetime)
        => lifetime is { } duration ? DateTimeOffset.UtcNow.Add(duration) : DateTimeOffset.MaxValue;

    private static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new InvalidOperationException("Salesforce did not issue a valid bearer token.");
        }
    }

    public void Dispose()
    {
        _oauthHttp.Dispose();
        foreach (var slot in _credentials.Values)
        {
            slot.Gate.Dispose();
        }
        _credentials.Clear();
        _pending.Clear();
    }

    private sealed class CredentialSlot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal string? AccessToken;
        internal string? RefreshToken;
        internal DateTimeOffset ExpiresAt;
    }

    private sealed class PendingLogin(AgentTurnContext context, UserActionRequest action)
    {
        internal readonly AgentTurnContext Context = context;
        internal UserActionRequest Action = action;
        internal int Status;
        internal int Finishing;
        internal int CompletionRequested;
    }
}
