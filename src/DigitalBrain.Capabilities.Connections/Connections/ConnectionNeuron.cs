using System.Reflection;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Sealing;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Rules;

namespace DigitalBrain.Capabilities.Connections;

// Every connector's connection neuron derives from this. The credential is sealed state; the hooks
// receive it opened for one connector call inside the turn and never return it. A plain neuron (06-08): its state is the
// connector's ConnectionState record (clean break, R4-24: users connect again).
public abstract class ConnectionNeuron<TState> : Neuron<TState>, IConnection, IConnectionInbound
    where TState : ConnectionState, new()
{
    private const string ValueSecret = "value";
    private const string AccessSecret = "access";
    private const string RefreshSecret = "refresh";
    private static readonly TimeSpan RefreshLead = TimeSpan.FromSeconds(30);

    private (string Brain, string Connector, string Connection)? _key;
    protected (string Brain, string Connector, string Connection) Key => _key ??= OwnKey();

    // The state, as the connectors read and change it in place before a save.
    protected TState Snapshot => State;

    // The canonical full key "{brain}/{key}" the neuron was addressed by.
    protected string FullKey => $"{Id.Brain.Value}/{Id.Key}";

    protected IServiceProvider ServiceProvider => Context.Services;

    internal static IDisposable StampCaller(CapabilityCaller? caller) => CallerAdmission.Stamp(caller);

    // Facts fault the writes of a connection here (IConnectionWriteFaults); a production silo registers none.
    private IConnectionWriteFaults? Faults => GetService<IConnectionWriteFaults>();

    protected async Task PublishAsync(Signal signal)
    {
        var faults = Faults;
        if (faults is not null) { await faults.BeforeWrite(FullKey, null, [signal]); }
        await Publish(signal);
        if (faults is not null) { await faults.AfterWrite(FullKey, null, [signal]); }
    }

    // Every save of a connection: the state and its signals as one journal entry.
    protected async Task Commit(TState next, params Signal[] signals)
    {
        var faults = Faults;
        if (faults is not null) { await faults.BeforeWrite(FullKey, next, signals); }
        await Save(next, signals);
        if (faults is not null) { await faults.AfterWrite(FullKey, next, signals); }
    }

    // There is no save without a signal (06-08): a change that moves no status says the connection was updated.
    protected Task SaveQuietly(TState next) => Commit(next, new ConnectionUpdated(Key.Connector, Key.Connection));

    // The state as loaded is the facts' view of the last committed state; the connector's own activation runs after.
    protected sealed override Task OnActivate(CancellationToken cancellationToken)
    {
        Faults?.Loaded(FullKey, State);
        return Activated(cancellationToken);
    }

    protected virtual Task Activated(CancellationToken cancellationToken) => Task.CompletedTask;

    // The connectors change the live state in place before a save; a save that failed puts the values it held back
    // (a plain neuron's State has no setter, so the copy is member by member).
    protected void Restore(TState before)
    {
        ArgumentNullException.ThrowIfNull(before);
        foreach (var property in typeof(TState).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property is { CanRead: true, CanWrite: true } && property.GetIndexParameters().Length == 0)
            { property.SetValue(Snapshot, property.GetValue(before)); }
        }
    }

    // ConnectionKeys.IdFor resolves a key by its connector, so the connector is this neuron's grain type;
    // a key reaching another type (an explicit GetGrain) is refused rather than served as the wrong connector.
    private (string, string, string) OwnKey()
    {
        var key = ConnectionKeys.Parse(FullKey);
        if (key.ConnectorId != Id.Type)
        { throw new ArgumentException("A connection key must name the connector this neuron serves.", "key"); }
        return key;
    }

    private ISecretSealer Sealer => GetRequiredService<ISecretSealer>();
    private IBrainSecretSealer? BrainSealer => GetService<IBrainSecretSealer>();
    private TimeProvider Clock => GetService<TimeProvider>() ?? Context.Time;
    private ConnectorDefinition Connector => GetRequiredService<IReadOnlyList<ConnectorDefinition>>()
        .FirstOrDefault(c => c.Id == Key.Connector) ?? throw new InvalidOperationException($"No composed module declares connector '{Key.Connector}'.");

    // The module answers: does this credential still work?
    protected abstract Task<ConnectionProbeResult> ProbeAsync(Credential credential, CancellationToken ct);

    // The module may parse external fields (instance url, mailbox) and refuse a grant.
    protected virtual Task<OAuthGrant> AcceptGrantAsync(OAuthGrant grant) => Task.FromResult(grant);

    // Standard refresh_token grant through the Platform; GitHub overrides.
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
        // An OAuth method's token arrives only through AcceptGrant, after the Platform's code exchange.
        await Check("Connect", new AuthMethodIsNotBrowserLogin(Key.Connector, authMethodId, Connector.OAuthMethods.Any(method => method.Id == authMethodId)));
        Snapshot.AuthMethodId = authMethodId;
        Snapshot.Secrets = new(StringComparer.Ordinal) { [ValueSecret] = await SealAsync(ValueSecret, value) };
        Snapshot.ExpiresAt = null;
        Snapshot.Scopes = "";
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
        // The module's override sets Snapshot.Account (mailbox, instance url, owner/repo) and may throw.
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
        // The message may carry an external URL with a token in it; only the type is kept.
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
        // The hook runs while Account is still known, so an index keyed by it can be cleaned.
        await OnDisconnectedAsync();
        Snapshot.Secrets = new(StringComparer.Ordinal);
        Snapshot.ExpiresAt = null;
        Snapshot.Account = null;
        await Transition(ConnectionStatus.Disconnected, null, notifyCatalog: false);
        await Context.Get<IConnectionIndex>(IConnectionIndex.Id(Key.Brain)).Untrack(FullKey);
        await NotifyCatalog();
    }

    public async Task<ConnectionView> Adopt(string authMethodId, string? account)
    {
        RequireNotAppCaller();
        await RequireAuthMethod("Adopt", authMethodId);
        if (Snapshot.Status != ConnectionStatus.Disconnected || Snapshot.Secrets.Count > 0) { return View(); }
        Snapshot.AuthMethodId = authMethodId;
        Snapshot.Account = account;
        await Transition(ConnectionStatus.Disconnected, "Shared into this brain; its credential is not sealed here yet.", notifyCatalog: false);
        await Track();
        await NotifyCatalog();
        return View();
    }

    // R130: this connection's own credential, opened here and re-sealed under the target brain's core part key. The
    // plaintext lives only inside this turn; what leaves is ciphertext.
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

    // R130/R147: fills the shared copy's credential from secrets already sealed under this brain's core part key.
    public async Task<ConnectionView> Import(string authMethodId, string? account, Dictionary<string, SealedSecret> secrets, ConnectionStatus status, string? detail)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        RequireNotAppCaller();
        await RequireAuthMethod("Import", authMethodId);
        Snapshot.AuthMethodId = authMethodId;
        Snapshot.Account = account;
        Snapshot.Secrets = new(secrets, StringComparer.Ordinal);
        await Transition(status, detail, notifyCatalog: false);
        await Track();
        await NotifyCatalog();
        return View();
    }

    // ShareInto's entry is the one ShareWith records for that app's principal when the app key is of that brain,
    // so both name the same sharing; the alias keeps the exact pre-Phase F entry for any pairing.
    public Task ShareInto(string brainId, string app) => Add(AppEntry(brainId, app));

    public Task UnshareFrom(string brainId, string app) => Remove(AppEntry(brainId, app));

    public Task<bool> IsSharedInto(string brainId, string app) => Task.FromResult(Snapshot.SharedInto.Contains(AppEntry(brainId, app), StringComparer.Ordinal));

    public Task ShareWith(string principalId) => Add(SharingEntry(principalId));

    public Task UnshareWith(string principalId) => Remove(SharingEntry(principalId));

    public Task<bool> IsSharedWith(string principalId) => Task.FromResult(Snapshot.SharedInto.Contains(SharingEntry(principalId), StringComparer.Ordinal));

    private async Task Add(string entry)
    {
        if (Snapshot.SharedInto.Contains(entry, StringComparer.Ordinal)) { return; }
        Snapshot.SharedInto = [.. Snapshot.SharedInto, entry];
        await SaveQuietly(Snapshot);
    }

    private async Task Remove(string entry)
    {
        if (!Snapshot.SharedInto.Contains(entry, StringComparer.Ordinal)) { return; }
        Snapshot.SharedInto = [.. Snapshot.SharedInto.Where(shared => shared != entry)];
        await SaveQuietly(Snapshot);
    }

    private static string AppEntry(string brainId, string app)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        return $"{brainId}/{app}";
    }

    // The persisted SharedInto entries keep their pre-Phase F shape: an app principal is "{brain}/{appKey}", its
    // brain being the one its key names; any other principal is "principal:{id}", a shape no earlier entry has.
    // No stored entry changes meaning, so no migration is needed.
    internal static string SharingEntry(string principalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        return AppPrincipal.AppKeyOf(principalId) is { } appKey && appKey.IndexOf('/', StringComparison.Ordinal) is > 0 and var slash
            ? AppEntry(appKey[..slash], appKey)
            : "principal:" + principalId;
    }

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

    // R130: a shared copy's secret is sealed under its brain's core part key; the owner's own under the master key.
    private async Task<string> OpenSecret(SealedSecret secret)
        => BrainSeal.IsBrainSealed(secret) && BrainSealer is { } part
            ? await part.Open(Key.Brain, secret)
            : Sealer.Open(secret);

    // R130: a shared copy's secrets are sealed under its brain's core part key; the owner's own under the master
    // key. Every write that replaces them stays under the same key the copy already carries.
    private bool BrainSealed => BrainSealer is not null && Snapshot.Secrets.Values.Any(BrainSeal.IsBrainSealed);

    private async Task<SealedSecret> SealAsync(string name, string plaintext)
    {
        if (BrainSealed && BrainSealer is { } part) { return await part.Seal(Key.Brain, name, plaintext); }
        return Sealer.Seal(name, plaintext);
    }

    // A refresh response usually omits the refresh token (Google does), so a refresh keeps the sealed
    // one unless the connector rotates it. A new grant replaces both, and without one clears it.
    private async Task SealAsync(OAuthGrant grant, bool keepRefresh)
    {
        var refresh = grant.RefreshToken is { } rotated ? await SealAsync(RefreshSecret, rotated)
            : keepRefresh && Snapshot.Secrets.TryGetValue(RefreshSecret, out var existing) ? existing
            : null;
        var access = await SealAsync(AccessSecret, grant.AccessToken);
        var secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
        {
            [AccessSecret] = access,
        };
        if (refresh is not null)
        {
            secrets[RefreshSecret] = refresh;
        }
        Snapshot.Secrets = secrets;
        Snapshot.ExpiresAt = grant.ExpiresAt;
        Snapshot.Scopes = grant.Scopes;
    }

    // One function moves status and publishes, so no module can publish a status it did not persist.
    // Every operation that calls it publishes exactly one signal, even when the status repeats.
    private async Task Transition(ConnectionStatus status, string? detail, bool notifyCatalog = true)
    {
        Snapshot.Status = status;
        Snapshot.Detail = detail;
        Signal signal = status switch
        {
            ConnectionStatus.Connected => new ConnectionConnected(Key.Connector, Key.Connection, Snapshot.AuthMethodId, Snapshot.Account),
            ConnectionStatus.Expired => new ConnectionExpired(Key.Connector, Key.Connection),
            ConnectionStatus.Failing => new ConnectionFailing(Key.Connector, Key.Connection, detail ?? ""),
            _ => new ConnectionDisconnected(Key.Connector, Key.Connection),
        };
        await Commit(Snapshot, signal);
        if (notifyCatalog) { await NotifyCatalog(); }
    }

    private Task NotifyCatalog() => Context.Get<IConnectionCatalogUpdates>(IConnectionCatalogUpdates.Id(Key.Brain)).Changed(FullKey);

    private Task Track() => Context.Get<IConnectionIndex>(IConnectionIndex.Id(Key.Brain)).Track(FullKey);

    private Task RequireAuthMethod(string operation, string authMethodId)
        => Check(operation, new AuthMethodIsOffered(Key.Connector, authMethodId, Connector.AuthMethods.Contains(authMethodId, StringComparer.Ordinal)));

    // Members of the brain named in the key; never an installed app.
    private void RequireMember()
        => CallerAdmission.RequireBrainMember(Key.Brain, $"Only a member of the owning brain can manage connection {Key.Connector}/{Key.Connection}.");

    private static void RequireNotAppCaller()
        => CallerAdmission.RequireNotAppCaller("Zero-token auth enforcement: sandbox and app callers cannot export or import raw connection secrets.");

    private ConnectionView View() => new(Key.Connector, Key.Connection, Snapshot.AuthMethodId, Snapshot.Status, Snapshot.Account, Snapshot.ExpiresAt, Snapshot.LastProbedAt, Snapshot.Detail);

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
