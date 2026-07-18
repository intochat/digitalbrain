using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Brain.Kernel.Connections;

public sealed class ConnectionKind(IServiceProvider services) : INeuronKind
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AuthorizingWindow = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> KnownHealthValues = new(StringComparer.Ordinal)
    {
        ConnectionHealth.Healthy,
        ConnectionHealth.MissingAppCredentials,
        ConnectionHealth.NotConfigured,
        ConnectionHealth.NotAuthorized,
        ConnectionHealth.TokenExpired,
        ConnectionHealth.ProviderError,
        ConnectionHealth.NetworkError
    };

    public string Kind => "connection";

    public string[] Contracts =>
    [
        "connection.start-auth.v1",
        "connection.complete-auth.v1",
        "connection.probe.v1",
        "connection.suspend.v1",
        "connection.resume.v1",
        "connection.lease-token.v1"
    ];

    private TimeProvider Time => services.GetService<TimeProvider>() ?? TimeProvider.System;
    private IConnectionTokenProtector TokenProtector => services.GetRequiredService<IConnectionTokenProtector>();

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "connection.start-auth.v1" => HandleStartAuthAsync(context, invocation.InputJson),
            "connection.complete-auth.v1" => HandleCompleteAuthAsync(context, invocation.InputJson),
            "connection.probe.v1" => HandleProbeAsync(context, invocation.InputJson),
            "connection.suspend.v1" => HandleSuspendAsync(context, invocation.InputJson),
            "connection.resume.v1" => HandleResumeAsync(context, invocation.InputJson),
            "connection.lease-token.v1" => HandleLeaseTokenAsync(context, invocation.InputJson),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection)
    {
        var folded = Fold(context.Journal);
        var suspended = folded.State == ConnectionState.Suspended;
        var health = folded.LastHealth ?? (folded.State == ConnectionState.Connected ? ConnectionHealth.Healthy : ConnectionHealth.NotAuthorized);
        var fix = suspended ? "none" : FixFor(health);

        return JsonSerializer.Serialize(new
        {
            state = StateName(folded),
            health,
            fix,
            authorizingExpiresAt = folded.Authorization?.ExpiresAt,
            expiresAt = folded.TokenExpiresAt,
            suspended
        }, JsonOptions);
    }

    private ValueTask<KindResult> HandleStartAuthAsync(NeuronContext context, string inputJson)
    {
        EnsureWellFormedJson(inputJson);
        var provider = RequireProvider(context.Address);
        var folded = Fold(context.Journal);
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var authorizationUrl = provider.BuildAuthorizationUrl(state);
        var expiresAt = Time.GetUtcNow() + AuthorizingWindow;
        var eventKind = folded.State == ConnectionState.Connected ? "connection.reauthorizing" : "connection.authorizing";
        var payload = JsonSerializer.Serialize(
            new ConnectionAuthorizationState(StateDigest(state), expiresAt),
            JsonOptions);
        var output = JsonSerializer.Serialize(new { authorizationUrl }, JsonOptions);

        return ValueTask.FromResult(new KindResult(output, [(eventKind, payload)]));
    }

    private async ValueTask<KindResult> HandleCompleteAuthAsync(NeuronContext context, string inputJson)
    {
        var code = RequireStringField(inputJson, "code");
        var state = RequireStringField(inputJson, "state");
        var folded = Fold(context.Journal);
        if (folded.State != ConnectionState.Authorizing
            || folded.Authorization is not { } authorization
            || Time.GetUtcNow() >= authorization.ExpiresAt
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(authorization.StateDigest),
                SHA256.HashData(Encoding.UTF8.GetBytes(state))))
            throw new BrainException(BrainErrors.ConnectionUnhealthy, $"{ConnectionHealth.NotAuthorized}: complete-auth requires an unexpired authorizing state");

        var provider = RequireProvider(context.Address);
        ConnectionToken token;
        try
        {
            token = await provider.ExchangeCodeAsync(code, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not BrainException)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }

        var payload = JsonSerializer.Serialize(
            new ConnectedPayload(
                TokenProtector.Protect(context.Address, token),
                token.ExpiresAt,
                token.InstanceUrl),
            JsonOptions);
        var output = JsonSerializer.Serialize(new { status = "connected" }, JsonOptions);
        return new KindResult(output, [("connection.connected", payload)]);
    }

    private async ValueTask<KindResult> HandleProbeAsync(NeuronContext context, string inputJson)
    {
        EnsureWellFormedJson(inputJson);
        var folded = Fold(context.Journal);

        string health;
        string detail;
        if (folded.State == ConnectionState.Suspended)
        {
            health = folded.LastHealth ?? ConnectionHealth.NotAuthorized;
            detail = "connection is suspended";
        }
        else if (folded.State != ConnectionState.Connected)
        {
            health = ConnectionHealth.NotAuthorized;
            detail = "not connected";
        }
        else
        {
            var provider = RequireProvider(context.Address);
            ProbeResult probeResult;
            try
            {
                var token = TokenProtector.Unprotect(context.Address, folded.ProtectedToken!);
                probeResult = await provider.ProbeAsync(token, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not BrainException)
            {
                probeResult = new ProbeResult(ConnectionHealth.ProviderError, ex.Message);
            }
            health = probeResult.Health;
            detail = probeResult.Detail;
            if (!KnownHealthValues.Contains(health))
            {
                detail = $"invalid health '{health}': {detail}";
                health = ConnectionHealth.ProviderError;
            }
        }

        var suspended = folded.State == ConnectionState.Suspended;
        var fix = suspended ? "none" : FixFor(health);
        var payload = JsonSerializer.Serialize(new { health, fix, detail, suspended }, JsonOptions);
        return new KindResult(payload, [("connection.probed", payload)]);
    }

    private static ValueTask<KindResult> HandleSuspendAsync(NeuronContext context, string inputJson)
    {
        var reason = RequireStringField(inputJson, "reason");
        var folded = Fold(context.Journal);
        if (folded.State == ConnectionState.Suspended)
            throw new BrainException(BrainErrors.ConnectionUnhealthy, "connection is already suspended");

        var payload = JsonSerializer.Serialize(new { reason }, JsonOptions);
        return ValueTask.FromResult(new KindResult("{}", [("connection.suspended", payload)]));
    }

    private static ValueTask<KindResult> HandleResumeAsync(NeuronContext context, string inputJson)
    {
        var reason = RequireStringField(inputJson, "reason");
        var folded = Fold(context.Journal);
        if (folded.State != ConnectionState.Suspended)
            throw new BrainException(BrainErrors.ConnectionUnhealthy, "connection is not suspended");

        var payload = JsonSerializer.Serialize(new { reason }, JsonOptions);
        return ValueTask.FromResult(new KindResult("{}", [("connection.resumed", payload)]));
    }

    private ValueTask<KindResult> HandleLeaseTokenAsync(NeuronContext context, string inputJson)
    {
        EnsureWellFormedJson(inputJson);
        var caller = NeuronAddress.Parse(context.CallerKey);
        if (caller.NeuronId.StartsWith("session/", StringComparison.Ordinal)
            || caller.SpaceId != context.Address.SpaceId
            || caller.OwnerId != context.Address.OwnerId
            || !context.Synapses.Any(s =>
                s.Relation == SynapseRelation.Grants
                && s.TargetKey == context.CallerKey
                && s.Constraint == "connection.lease-token.v1"))
            throw new BrainException(BrainErrors.GrantMissing, $"{context.CallerKey} cannot lease tokens");

        var folded = Fold(context.Journal);
        if (folded.State != ConnectionState.Connected || folded.ProtectedToken is not { } protectedToken)
            throw new BrainException(BrainErrors.ConnectionUnhealthy, $"{ConnectionHealth.NotAuthorized}: connection is not connected");

        var token = TokenProtector.Unprotect(context.Address, protectedToken);
        var output = JsonSerializer.Serialize(token, JsonOptions);
        return ValueTask.FromResult(new KindResult(output, [], TransientReceipt: true));
    }

    private IConnectionProvider RequireProvider(NeuronAddress address)
    {
        var providerName = ProviderName(address);
        return services.GetKeyedService<IConnectionProvider>(providerName)
            ?? throw new BrainException(BrainErrors.ConnectionUnhealthy, $"{ConnectionHealth.MissingAppCredentials}: no provider registered for '{providerName}'");
    }

    private static string ProviderName(NeuronAddress address)
    {
        var slash = address.NeuronId.IndexOf('/');
        var tail = slash < 0 ? address.NeuronId : address.NeuronId[(slash + 1)..];
        var dash = tail.IndexOf('-');
        return dash < 0 ? tail : tail[..dash];
    }

    private static string FixFor(string health) => health switch
    {
        ConnectionHealth.Healthy => "none",
        ConnectionHealth.TokenExpired => "reauthorize",
        ConnectionHealth.ProviderError => "retry",
        ConnectionHealth.NetworkError => "retry",
        _ => "connect"
    };

    private static string StateName(Folded folded) => folded.State switch
    {
        ConnectionState.NotConnected => "notConnected",
        ConnectionState.Authorizing => folded.WasConnectedBeforeAuth ? "reauthorizing" : "authorizing",
        ConnectionState.Connected => "connected",
        ConnectionState.Suspended => "suspended",
        _ => "notConnected"
    };

    private enum ConnectionState { NotConnected, Authorizing, Connected, Suspended }

    private sealed record Folded(
        ConnectionState State,
        string? ProtectedToken,
        DateTimeOffset? TokenExpiresAt,
        ConnectionAuthorizationState? Authorization,
        bool WasConnectedBeforeAuth,
        string? LastHealth);

    private sealed record ConnectedPayload(
        string ProtectedToken,
        DateTimeOffset ExpiresAt,
        string? InstanceUrl);

    private static Folded Fold(IReadOnlyList<NeuronEvent> journal)
    {
        var state = ConnectionState.NotConnected;
        string? protectedToken = null;
        DateTimeOffset? tokenExpiresAt = null;
        ConnectionAuthorizationState? authorization = null;
        var wasConnectedBeforeAuth = false;
        string? lastHealth = null;
        var preSuspendState = ConnectionState.NotConnected;

        foreach (var evt in journal)
        {
            switch (evt.Kind)
            {
                case "connection.authorizing":
                case "connection.reauthorizing":
                    wasConnectedBeforeAuth = evt.Kind == "connection.reauthorizing";
                    state = ConnectionState.Authorizing;
                    authorization = JsonSerializer.Deserialize<ConnectionAuthorizationState>(evt.PayloadJson, JsonOptions);
                    break;
                case "connection.connected":
                    var connected = JsonSerializer.Deserialize<ConnectedPayload>(evt.PayloadJson, JsonOptions)!;
                    protectedToken = connected.ProtectedToken;
                    tokenExpiresAt = connected.ExpiresAt;
                    state = ConnectionState.Connected;
                    authorization = null;
                    break;
                case "connection.probed":
                    lastHealth = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson, JsonOptions).GetProperty("health").GetString();
                    break;
                case "connection.suspended":
                    preSuspendState = state;
                    state = ConnectionState.Suspended;
                    break;
                case "connection.resumed":
                    state = preSuspendState;
                    break;
            }
        }

        return new Folded(state, protectedToken, tokenExpiresAt, authorization, wasConnectedBeforeAuth, lastHealth);
    }

    private static string StateDigest(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void EnsureWellFormedJson(string inputJson)
    {
        try
        {
            using var _ = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }
    }

    private static string RequireStringField(string inputJson, string field)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
                throw new BrainException("input.invalid", $"{field} field is required");

            var value = element.GetString();
            if (string.IsNullOrWhiteSpace(value))
                throw new BrainException("input.invalid", $"{field} cannot be empty");

            return value;
        }
    }
}
