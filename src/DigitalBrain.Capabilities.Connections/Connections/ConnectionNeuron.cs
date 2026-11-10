using System.Reflection;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Sealing;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Rules;

namespace DigitalBrain.Capabilities.Connections;

public abstract class ConnectionNeuron<TState> : Neuron<TState>, IConnection, IConnectionInbound
    where TState : ConnectionState, new()
{
    private const string ValueSecret = ConnectionState.ValueSecret;
    private const string AccessSecret = ConnectionState.AccessSecret;
    private const string RefreshSecret = ConnectionState.RefreshSecret;
    private static readonly TimeSpan RefreshLead = TimeSpan.FromSeconds(30);

    private ConnectionAddress? _address;
    private ConnectionAddress Address => _address ??= ResolveAddress();
    protected (string Brain, string Connector, string Connection) Key => Address.ToTuple();

    protected TState Snapshot => State;

    protected string FullKey => $"{Id.Brain.Value}/{Id.Key}";

    protected IServiceProvider ServiceProvider => Context.Services;

    internal static IDisposable StampCaller(CapabilityCaller? caller) => CallerAdmission.Stamp(caller);

    private IConnectionWriteFaults? Faults => GetService<IConnectionWriteFaults>();

    protected async Task PublishAsync(Signal signal)
    {
        var faults = Faults;
        if (faults is not null) { await faults.BeforeWrite(FullKey, null, [signal]); }
        await Publish(signal);
        if (faults is not null) { await faults.AfterWrite(FullKey, null, [signal]); }
    }

    protected async Task Commit(TState next, params Signal[] signals)
    {
        var faults = Faults;
        if (faults is not null) { await faults.BeforeWrite(FullKey, next, signals); }
        await Save(next, signals);
        if (faults is not null) { await faults.AfterWrite(FullKey, next, signals); }
    }

    protected Task SaveQuietly(TState next) => Commit(next, new ConnectionUpdated(Address.ConnectorId, Address.ConnectionId));

    protected sealed override Task OnActivate(CancellationToken cancellationToken)
    {
        Faults?.Loaded(FullKey, State);
        return Activated(cancellationToken);
    }

    protected virtual Task Activated(CancellationToken cancellationToken) => Task.CompletedTask;

    protected void Restore(TState before)
    {
        ArgumentNullException.ThrowIfNull(before);
        Snapshot.CopyBaseFrom(before);
        foreach (var property in typeof(TState).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (property is { CanRead: true, CanWrite: true } && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(Snapshot, property.GetValue(before));
            }
        }
    }

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

    protected abstract Task<ConnectionProbeResult> ProbeAsync(Credential credential, CancellationToken ct);

    protected virtual Task<OAuthGrant> AcceptGrantAsync(OAuthGrant grant) => Task.FromResult(grant);

    protected virtual Task<OAuthGrant> RefreshAsync(Credential credential, CancellationToken ct)
        => credential.Refresh is null
            ? throw new InvalidOperationException("The connection has no refresh token.")
            : GetRequiredService<IOAuthTokenExchange>().RefreshAsync(FullKey, Snapshot.AuthMethodId, credential.Refresh, ct);

    protected virtual Task OnConnectedAsync() => Task.CompletedTask;
    protected virtual Task OnDisconnectedAsync() => Task.CompletedTask;

    public Task<ConnectionView> Read() => Task.FromResult(View());

    public async Task<ConnectionView> Connect(string authMethodId, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        RequireMember();
        await RequireAuthMethod("Connect", authMethodId);
        await Check("Connect", new AuthMethodIsNotBrowserLogin(Address.ConnectorId, authMethodId, Connector.OAuthMethods.Any(method => method.Id == authMethodId)));
        Snapshot.ApplySecretCredential(authMethodId, await SealAsync(ValueSecret, value));
        await Transition(ConnectionStatus.Connected, null, notifyCatalog: false);
        await Track();
        await NotifyCatalog();
        await OnConnectedAsync();
        return View();
    }

    public virtual async Task<ConnectionView> AcceptGrant(OAuthGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        RequireMember();
        await RequireAuthMethod("AcceptGrant", grant.AuthMethodId);
        var accepted = await AcceptGrantAsync(grant);
        await SealAsync(accepted, keepRefresh: false);
        Snapshot.AuthMethodId = accepted.AuthMethodId;
        await Transition(ConnectionStatus.Connected, null, notifyCatalog: false);
        await Track();
        await NotifyCatalog();
        await OnConnectedAsync();
        return View();
    }

    public async Task<ConnectionView> Probe()
    {
        RequireMember();
        await Check("Probe", new ConnectionIsConfigured(Snapshot.Status != ConnectionStatus.Disconnected));
        ConnectionProbeResult result;
        try { result = await ProbeAsync(await OpenAsync(), CancellationToken.None); }
        catch (Exception error) when (error is not OperationCanceledException)
        { result = ConnectionProbeResult.Failing($"The connector probe failed ({error.GetType().Name})."); }
        Snapshot.LastProbedAt = Clock.GetUtcNow();
        await Transition(result.Outcome switch
        {
            ConnectionProbeOutcome.Connected => ConnectionStatus.Connected,
            ConnectionProbeOutcome.Expired => ConnectionStatus.Expired,
            _ => ConnectionStatus.Failing,
        }, result.Detail);
        return View();
    }

    public async Task Disconnect()
    {
        RequireMember();
        await OnDisconnectedAsync();
        Snapshot.ClearCredentials();
        await Transition(ConnectionStatus.Disconnected, null, notifyCatalog: false);
        await Context.Get<IConnectionIndex>(IConnectionIndex.Id(Address.Brain.Value)).Untrack(FullKey);
        await NotifyCatalog();
    }

    public async Task<ConnectionView> Adopt(string authMethodId, string? account)
    {
        RequireNotAppCaller();
        await RequireAuthMethod("Adopt", authMethodId);
        if (Snapshot.IsConfigured) { return View(); }
        Snapshot.AdoptSharedMetadata(authMethodId, account);
        await Transition(ConnectionStatus.Disconnected, "Shared into this brain; its credential is not sealed here yet.", notifyCatalog: false);
        await Track();
        await NotifyCatalog();
        return View();
    }

    public async Task<Dictionary<string, SealedSecret>> ExportFor(string brainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        RequireNotAppCaller();
        if (Snapshot.Secrets.Count == 0) { return new(StringComparer.Ordinal); }
        var part = BrainSealer;
        await Check("ExportFor", new BrainSealerIsAvailable(part is not null));
        var reSealed = new Dictionary<string, SealedSecret>(Snapshot.Secrets.Count, StringComparer.Ordinal);
        foreach (var (name, secret) in Snapshot.Secrets)
        {
            reSealed[name] = await part!.Seal(brainId, name, Sealer.Open(secret));
        }
        return reSealed;
    }

    public async Task<ConnectionView> Import(string authMethodId, string? account, Dictionary<string, SealedSecret> secrets, ConnectionStatus status, string? detail)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        RequireNotAppCaller();
        await RequireAuthMethod("Import", authMethodId);
        Snapshot.ImportSealedCredentials(authMethodId, account, secrets);
        await Transition(status, detail, notifyCatalog: false);
        await Track();
        await NotifyCatalog();
        return View();
    }

    public Task ShareInto(string brainId, string app) => Add(SharedRecipient.ForApp(brainId, app));

    public Task UnshareFrom(string brainId, string app) => Remove(SharedRecipient.ForApp(brainId, app));

    public Task<bool> IsSharedInto(string brainId, string app) => Task.FromResult(Snapshot.IsSharedWith(SharedRecipient.ForApp(brainId, app)));

    public Task ShareWith(string principalId) => Add(SharedRecipient.ForPrincipal(principalId));

    public Task UnshareWith(string principalId) => Remove(SharedRecipient.ForPrincipal(principalId));

    public Task<bool> IsSharedWith(string principalId) => Task.FromResult(Snapshot.IsSharedWith(SharedRecipient.ForPrincipal(principalId)));

    private async Task Add(SharedRecipient recipient)
    {
        if (!Snapshot.TryAddSharedRecipient(recipient)) { return; }
        await SaveQuietly(Snapshot);
    }

    private async Task Remove(SharedRecipient recipient)
    {
        if (!Snapshot.TryRemoveSharedRecipient(recipient)) { return; }
        await SaveQuietly(Snapshot);
    }

    internal static string SharingEntry(string principalId) => SharedRecipient.ForPrincipal(principalId).Value;

    // Zero-token auth enforcement: opens the credential for one call inside the connection grain activation;
    // refreshes first when the access token is about to expire, and guarantees raw credentials never leave the turn.
    protected async Task<T> UseCredentialAsync<T>(Func<Credential, CancellationToken, Task<T>> call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        AssertZeroTokenReturnType<T>();
        if (Snapshot.Status == ConnectionStatus.Disconnected || Snapshot.Secrets.Count == 0)
        { throw new InvalidOperationException($"{Key.Connector}/{Key.Connection} is not connected."); }
        var credential = await OpenAsync();
        if (Snapshot.ExpiresAt is { } expiry && expiry <= Clock.GetUtcNow() + RefreshLead && credential.Refresh is not null)
        {
            var refreshed = await RefreshAsync(credential, ct);
            await SealAsync(refreshed, keepRefresh: true);
            await Transition(ConnectionStatus.Connected, null);
            credential = await OpenAsync();
        }
        var result = await call(credential, ct);
        AssertZeroTokenReturn(result, credential);
        return result;
    }

#pragma warning disable CA1000
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

    protected Task MarkExpired(string detail) => Transition(ConnectionStatus.Expired, detail);
    protected Task MarkFailing(string detail) => Transition(ConnectionStatus.Failing, detail);

    private async Task<Credential> OpenAsync()
    {
        string? value = null, refresh = null;
        foreach (var (name, secret) in Snapshot.Secrets)
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

    private bool BrainSealed => BrainSealer is not null && Snapshot.Secrets.Values.Any(BrainSeal.IsBrainSealed);

    private async Task<SealedSecret> SealAsync(string name, string plaintext)
    {
        if (BrainSealed && BrainSealer is { } part) { return await part.Seal(Address.Brain.Value, name, plaintext); }
        return Sealer.Seal(name, plaintext);
    }

    private async Task SealAsync(OAuthGrant grant, bool keepRefresh)
    {
        var refresh = grant.RefreshToken is { } rotated ? await SealAsync(RefreshSecret, rotated)
            : keepRefresh && Snapshot.Secrets.TryGetValue(RefreshSecret, out var existing) ? existing
            : null;
        var access = await SealAsync(AccessSecret, grant.AccessToken);
        Snapshot.ApplyOAuthCredential(grant, access, refresh);
    }

    private async Task Transition(ConnectionStatus status, string? detail, bool notifyCatalog = true)
    {
        Snapshot.Status = status;
        Snapshot.Detail = detail;
        Signal signal = status switch
        {
            ConnectionStatus.Connected => new ConnectionConnected(Address.ConnectorId, Address.ConnectionId, Snapshot.AuthMethodId, Snapshot.Account),
            ConnectionStatus.Expired => new ConnectionExpired(Address.ConnectorId, Address.ConnectionId),
            ConnectionStatus.Failing => new ConnectionFailing(Address.ConnectorId, Address.ConnectionId, detail ?? ""),
            _ => new ConnectionDisconnected(Address.ConnectorId, Address.ConnectionId),
        };
        await Commit(Snapshot, signal);
        if (notifyCatalog) { await NotifyCatalog(); }
    }

    private Task NotifyCatalog() => Context.Get<IConnectionCatalogUpdates>(IConnectionCatalogUpdates.Id(Address.Brain.Value)).Changed(FullKey);

    private Task Track() => Context.Get<IConnectionIndex>(IConnectionIndex.Id(Address.Brain.Value)).Track(FullKey);

    private Task RequireAuthMethod(string operation, string authMethodId)
        => Check(operation, new AuthMethodIsOffered(Address.ConnectorId, authMethodId, Connector.AuthMethods.Contains(authMethodId, StringComparer.Ordinal)));

    private void RequireMember()
        => CallerAdmission.RequireBrainMember(Address.Brain.Value, $"Only a member of the owning brain can manage connection {Address.ConnectorId}/{Address.ConnectionId}.");

    private static void RequireNotAppCaller()
        => CallerAdmission.RequireNotAppCaller("Zero-token auth enforcement: sandbox and app callers cannot export or import raw connection secrets.");

    private ConnectionView View() => new(Address.ConnectorId, Address.ConnectionId, Snapshot.AuthMethodId, Snapshot.Status, Snapshot.Account, Snapshot.ExpiresAt, Snapshot.LastProbedAt, Snapshot.Detail);

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
