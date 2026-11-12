using DigitalBrain.Neurons;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Connections.Catalog;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Sealing;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Rules;

namespace DigitalBrain.Capabilities.Connections;

/// <summary>Base neuron for connection instances managing credentials, probing, and lifecycle.</summary>
/// <typeparam name="TState">The connection state type deriving from <see cref="ConnectionState"/>.</typeparam>
public abstract class ConnectionNeuron<TState> : Neuron<TState>, IConnection, IConnectionInbound
    where TState : ConnectionState, new()
{
    private const string ValueSecret = ConnectionState.ValueSecret;
    private const string AccessSecret = ConnectionState.AccessSecret;
    private const string RefreshSecret = ConnectionState.RefreshSecret;
    private static readonly TimeSpan RefreshLead = TimeSpan.FromSeconds(30);

    private ConnectionAddress? _address;
    private ConnectionAddress Address => _address ??= ResolveAddress();

    /// <summary>The connection key tuple (Brain, Connector, Connection).</summary>
    protected (string Brain, string Connector, string Connection) Key => Address.ToTuple();

    /// <summary>The current state snapshot.</summary>
    protected TState Snapshot => State;

    /// <summary>The full connection key string.</summary>
    protected string FullKey => $"{Id.Brain.Value}/{Id.Key}";

    /// <summary>The seat's service container.</summary>
    protected IServiceProvider ServiceProvider => Seat.Services;

    internal static IDisposable StampCaller(CapabilityCaller? caller) => CallerAdmission.Stamp(caller);

    private bool _catalogued;

    /// <summary>Records next state and publishes the domain fact atomically.</summary>
    protected virtual async Task Record(TState next, Signal fact)
    {
        if (!_catalogued)
        {
            await Context.Get<IConnectionCatalog>(IConnectionCatalog.Id(Address.Brain.Value)).Subscribed().ConfigureAwait(true);
            _catalogued = true;
        }

        await Save(next, fact).ConfigureAwait(true);
    }

    /// <summary>Publishes an ephemeral or domain signal.</summary>
    protected Task PublishAsync(Signal signal) => Publish(signal);

    /// <summary>Commits state changes with associated signals.</summary>
    protected Task Commit(TState next, params Signal[] signals) => Save(next, signals);

    /// <summary>Saves state with a <see cref="ConnectionUpdated"/> fact.</summary>
    protected Task SaveQuietly(TState next) => Save(next, new ConnectionUpdated(Address.ConnectorId, Address.ConnectionId));

    /// <inheritdoc />
    protected sealed override Task OnActivate(CancellationToken cancellationToken)
        => Activated(cancellationToken);

    /// <summary>Called when the neuron is activated.</summary>
    protected virtual Task Activated(CancellationToken cancellationToken) => Task.CompletedTask;

    private ConnectionAddress ResolveAddress()
    {
        var address = ConnectionAddress.Parse(FullKey);
        if (address.ConnectorId != Id.Type)
        {
            throw new ArgumentException("A connection key must name the connector this neuron serves.", "key");
        }

        return address;
    }

    private ISecretSealer Sealer => GetRequiredService<ISecretSealer>();
    private IBrainSecretSealer? BrainSealer => GetService<IBrainSecretSealer>();
    private TimeProvider Clock => GetService<TimeProvider>() ?? Context.Time;
    private ConnectorDefinition Connector => GetRequiredService<IReadOnlyList<ConnectorDefinition>>()
        .FirstOrDefault(c => c.Id == Address.ConnectorId) ?? throw new InvalidOperationException($"No composed module declares connector '{Address.ConnectorId}'.");

    /// <summary>Probes the remote service with opened credentials to verify connection health.</summary>
    protected abstract Task<ConnectionProbeResult> ProbeAsync(Credential credential, CancellationToken ct);

    /// <summary>Hook called after an OAuth grant is received to allow connectors to transform or enrich it.</summary>
    protected virtual Task<OAuthGrant> AcceptGrantAsync(OAuthGrant grant) => Task.FromResult(grant);

    /// <summary>Hook called to refresh an expired OAuth access token.</summary>
    protected virtual Task<OAuthGrant> RefreshAsync(Credential credential, CancellationToken ct)
        => credential.Refresh is null
            ? throw new InvalidOperationException("The connection has no refresh token.")
            : GetRequiredService<IOAuthTokenExchange>().RefreshAsync(FullKey, State.AuthMethodId, credential.Refresh, ct);

    /// <summary>Called after successful connection establishment.</summary>
    protected virtual Task OnConnectedAsync() => Task.CompletedTask;

    /// <summary>Called when disconnecting.</summary>
    protected virtual Task OnDisconnectedAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task<ConnectionView> Read() => Task.FromResult(View());

    /// <inheritdoc />
    public async Task<ConnectionView> Connect(string authMethodId, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        RequireMember();
        await RequireAuthMethod("Connect", authMethodId);
        await Check("Connect", new AuthMethodIsNotBrowserLogin(Address.ConnectorId, authMethodId, Connector.OAuthMethods.Any(method => method.Id == authMethodId)));
        var sealedVal = await SealAsync(ValueSecret, value);
        var (next, fact) = State.Connect(Address.ConnectorId, Address.ConnectionId, authMethodId, sealedVal);
        await Record(next, fact);
        await OnConnectedAsync();
        return View();
    }

    /// <inheritdoc />
    public virtual async Task<ConnectionView> AcceptGrant(OAuthGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        RequireMember();
        await RequireAuthMethod("AcceptGrant", grant.AuthMethodId);
        var accepted = await AcceptGrantAsync(grant);
        var access = await SealAsync(AccessSecret, accepted.AccessToken);
        var refresh = accepted.RefreshToken is { } rotated ? await SealAsync(RefreshSecret, rotated) : null;
        var (next, fact) = State.ConnectOAuth(Address.ConnectorId, Address.ConnectionId, accepted, access, refresh);
        await Record(next, fact);
        await OnConnectedAsync();
        return View();
    }

    /// <inheritdoc />
    public async Task<ConnectionView> Probe()
    {
        RequireMember();
        await Check("Probe", new ConnectionIsConfigured(State.Status != ConnectionStatus.Disconnected));
        ConnectionProbeResult result;
        try { result = await ProbeAsync(await OpenAsync(), CancellationToken.None); }
        catch (Exception error) when (error is not OperationCanceledException)
        { result = ConnectionProbeResult.Failing($"The connector probe failed ({error.GetType().Name})."); }
        var probedAt = Clock.GetUtcNow();
        var (next, fact) = result.Outcome switch
        {
            ConnectionProbeOutcome.Connected => (
                State with { Status = ConnectionStatus.Connected, LastProbedAt = probedAt, Detail = result.Detail },
                (Signal)new ConnectionConnected(Address.ConnectorId, Address.ConnectionId, State.AuthMethodId, State.Account, probedAt)),
            ConnectionProbeOutcome.Expired => State.Expire(Address.ConnectorId, Address.ConnectionId, result.Detail, probedAt),
            _ => State.Fail(Address.ConnectorId, Address.ConnectionId, result.Detail ?? "The probe reported failing status.", probedAt),
        };
        await Record(next, fact);
        return View();
    }

    /// <inheritdoc />
    public async Task Disconnect()
    {
        RequireMember();
        await OnDisconnectedAsync();
        var (next, fact) = State.Disconnect(Address.ConnectorId, Address.ConnectionId);
        await Record(next, fact);
    }

    /// <inheritdoc />
    public async Task<ConnectionView> Adopt(string authMethodId, string? account)
    {
        RequireNotAppCaller();
        await RequireAuthMethod("Adopt", authMethodId);
        if (State.IsConfigured) { return View(); }
        var (next, fact) = State.Adopt(Address.ConnectorId, Address.ConnectionId, authMethodId, account);
        await Record(next, fact);
        return View();
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, SealedSecret>> ExportFor(string brainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        RequireNotAppCaller();
        if (State.Secrets.Count == 0) { return new(StringComparer.Ordinal); }
        var part = BrainSealer;
        await Check("ExportFor", new BrainSealerIsAvailable(part is not null));
        var reSealed = new Dictionary<string, SealedSecret>(State.Secrets.Count, StringComparer.Ordinal);
        foreach (var (name, secret) in State.Secrets)
        {
            reSealed[name] = await part!.Seal(brainId, name, Sealer.Open(secret));
        }
        return reSealed;
    }

    /// <inheritdoc />
    public async Task<ConnectionView> Import(string authMethodId, string? account, Dictionary<string, SealedSecret> secrets, ConnectionStatus status, string? detail)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        RequireNotAppCaller();
        await RequireAuthMethod("Import", authMethodId);
        var next = State with
        {
            AuthMethodId = authMethodId,
            Account = account,
            Secrets = new Dictionary<string, SealedSecret>(secrets, StringComparer.Ordinal),
            Status = status,
            Detail = detail,
        };
        Signal fact = status switch
        {
            ConnectionStatus.Connected => new ConnectionConnected(Address.ConnectorId, Address.ConnectionId, authMethodId, account),
            ConnectionStatus.Expired => new ConnectionExpired(Address.ConnectorId, Address.ConnectionId),
            ConnectionStatus.Failing => new ConnectionFailing(Address.ConnectorId, Address.ConnectionId, detail ?? ""),
            _ => new ConnectionDisconnected(Address.ConnectorId, Address.ConnectionId),
        };
        await Record(next, fact);
        return View();
    }

    /// <inheritdoc />
    public Task ShareInto(string brainId, string app) => Add(SharedRecipient.ForApp(brainId, app));

    /// <inheritdoc />
    public Task UnshareFrom(string brainId, string app) => Remove(SharedRecipient.ForApp(brainId, app));

    /// <inheritdoc />
    public Task<bool> IsSharedInto(string brainId, string app) => Task.FromResult(State.IsSharedWith(SharedRecipient.ForApp(brainId, app)));

    /// <inheritdoc />
    public Task ShareWith(string principalId) => Add(SharedRecipient.ForPrincipal(principalId));

    /// <inheritdoc />
    public Task UnshareWith(string principalId) => Remove(SharedRecipient.ForPrincipal(principalId));

    /// <inheritdoc />
    public Task<bool> IsSharedWith(string principalId) => Task.FromResult(State.IsSharedWith(SharedRecipient.ForPrincipal(principalId)));

    private async Task Add(SharedRecipient recipient)
    {
        var (next, fact) = State.Share(Address.ConnectorId, Address.ConnectionId, recipient.Value);
        if (ReferenceEquals(next, State)) { return; }
        await Record(next, fact);
    }

    private async Task Remove(SharedRecipient recipient)
    {
        var (next, fact) = State.Unshare(Address.ConnectorId, Address.ConnectionId, recipient.Value);
        if (ReferenceEquals(next, State)) { return; }
        await Record(next, fact);
    }

    internal static string SharingEntry(string principalId) => SharedRecipient.ForPrincipal(principalId).Value;

    /// <summary>
    /// Zero-token auth enforcement: opens the credential for one call inside the connection grain activation;
    /// refreshes first when the access token is about to expire, and guarantees raw credentials never leave the turn.
    /// </summary>
    protected async Task<T> UseCredentialAsync<T>(Func<Credential, CancellationToken, Task<T>> call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        AssertZeroTokenReturnType<T>();
        if (State.Status == ConnectionStatus.Disconnected || State.Secrets.Count == 0)
        { throw new InvalidOperationException($"{Key.Connector}/{Key.Connection} is not connected."); }
        var credential = await OpenAsync();
        if (State.ExpiresAt is { } expiry && expiry <= Clock.GetUtcNow() + RefreshLead && credential.Refresh is not null)
        {
            var refreshed = await RefreshAsync(credential, ct);
            var access = await SealAsync(AccessSecret, refreshed.AccessToken);
            var refresh = refreshed.RefreshToken is { } rotated
                ? await SealAsync(RefreshSecret, rotated)
                : State.Secrets.TryGetValue(RefreshSecret, out var existing) ? existing : null;

            var secrets = new Dictionary<string, SealedSecret>(State.Secrets, StringComparer.Ordinal)
            {
                [AccessSecret] = access,
            };
            if (refresh is not null)
            {
                secrets[RefreshSecret] = refresh;
            }

            var (next, fact) = State.UpdateSecrets(Address.ConnectorId, Address.ConnectionId, secrets, refreshed.ExpiresAt);
            await Record(next, fact);
            credential = await OpenAsync();
        }
        var result = await call(credential, ct);
        AssertZeroTokenReturn(result, credential);
        return result;
    }

#pragma warning disable CA1000
    /// <summary>Validates that the return type T cannot leak raw credentials.</summary>
    public static void AssertZeroTokenReturnType<T>()
    {
        var type = typeof(T);
        if (type == typeof(Credential)
            || type == typeof(OAuthGrant)
            || type == typeof(SealedSecret)
            || type == typeof(SealedSecret[])
            || type == typeof(Dictionary<string, SealedSecret>)
            || type == typeof(IReadOnlyDictionary<string, SealedSecret>))
        {
            throw new InvalidOperationException("Zero-token auth enforcement: UseCredentialAsync may never return raw credentials or OAuth grants.");
        }
    }

    /// <summary>Validates that the returned value does not leak raw credential strings.</summary>
    public static void AssertZeroTokenReturn<T>(T result, Credential credential)
    {
        AssertZeroTokenReturnType<T>();
        if (result is null || credential is null) { return; }
        if (result is Credential or OAuthGrant or SealedSecret or SealedSecret[] or Dictionary<string, SealedSecret> or IReadOnlyDictionary<string, SealedSecret>)
        {
            throw new InvalidOperationException("Zero-token auth enforcement: UseCredentialAsync may never return raw credentials or OAuth grants.");
        }
        if (result is string text && (!string.IsNullOrEmpty(credential.Value) && text.Contains(credential.Value, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(credential.Refresh) && text.Contains(credential.Refresh, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException("Zero-token auth enforcement: UseCredentialAsync result may not leak the unsealed credential value.");
        }
    }
#pragma warning restore CA1000

    /// <summary>Transitions to expired with detail.</summary>
    protected async Task MarkExpired(string detail)
    {
        var (next, fact) = State.Expire(Address.ConnectorId, Address.ConnectionId, detail);
        await Record(next, fact);
    }

    /// <summary>Transitions to failing with detail.</summary>
    protected async Task MarkFailing(string detail)
    {
        var (next, fact) = State.Fail(Address.ConnectorId, Address.ConnectionId, detail);
        await Record(next, fact);
    }

    private async Task<Credential> OpenAsync()
    {
        string? value = null, refresh = null;
        foreach (var (name, secret) in State.Secrets)
        {
            var opened = await OpenSecret(secret);
            if (name is ValueSecret or AccessSecret) { value = opened; }
            else if (name == RefreshSecret) { refresh = opened; }
        }
        return new(value ?? throw new InvalidOperationException("The connection holds no credential."), refresh);
    }

    private async Task<string> OpenSecret(SealedSecret secret)
        => BrainSeal.IsBrainSealed(secret) && BrainSealer is { } part
            ? await part.Open(Address.Brain.Value, secret)
            : Sealer.Open(secret);

    private bool BrainSealed => BrainSealer is not null && State.Secrets.Values.Any(BrainSeal.IsBrainSealed);

    private async Task<SealedSecret> SealAsync(string name, string plaintext)
    {
        if (BrainSealed && BrainSealer is { } part) { return await part.Seal(Address.Brain.Value, name, plaintext); }
        return Sealer.Seal(name, plaintext);
    }

    private Task RequireAuthMethod(string operation, string authMethodId)
        => Check(operation, new AuthMethodIsOffered(Address.ConnectorId, authMethodId, Connector.AuthMethods.Contains(authMethodId, StringComparer.Ordinal)));

    private void RequireMember()
        => CallerAdmission.RequireBrainMember(Address.Brain.Value, $"Only a member of the owning brain can manage connection {Address.ConnectorId}/{Address.ConnectionId}.");

    private static void RequireNotAppCaller()
        => CallerAdmission.RequireNotAppCaller("Zero-token auth enforcement: sandbox and app callers cannot export or import raw connection secrets.");

    private ConnectionView View() => new(Address.ConnectorId, Address.ConnectionId, State.AuthMethodId, State.Status, State.Account, State.ExpiresAt, State.LastProbedAt, State.Detail);

    private T? GetService<T>() where T : class => ServiceProvider.GetService(typeof(T)) as T;

    private T GetRequiredService<T>() where T : class
        => ServiceProvider.GetService(typeof(T)) as T
            ?? throw new InvalidOperationException($"No service for type '{typeof(T)}' has been registered.");
}

internal sealed record AuthMethodIsNotBrowserLogin(string ConnectorId, string AuthMethodId, bool IsBrowserLogin) : Rule("identity.auth-method-not-browser-login")
{
    public override bool Broken => IsBrowserLogin;
    public override string Message => $"Authentication method '{AuthMethodId}' of connector '{ConnectorId}' is a browser login; start it from the login route.";
}

internal sealed record ConnectionIsConfigured(bool Configured) : Rule("identity.connection-configured")
{
    public override bool Broken => !Configured;
    public override string Message => "The connection is not configured.";
}

internal sealed record BrainSealerIsAvailable(bool Available) : Rule("identity.brain-sealer-available")
{
    public override bool Broken => !Available;
    public override string Message => "Re-sealing a shared credential needs this machine's part keys.";
}

internal sealed record AuthMethodIsOffered(string ConnectorId, string AuthMethodId, bool Offered) : Rule("identity.auth-method-offered")
{
    public override bool Broken => !Offered;
    public override string Message => $"Connector '{ConnectorId}' does not offer authentication method '{AuthMethodId}'.";
}
