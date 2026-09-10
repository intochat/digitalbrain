using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Google;

[GrainType("gmail")]
internal sealed class GmailNeuron(
    NeuronRuntime runtime,
    IGmailProvider provider,
    GmailTokenRefresh tokenRefresh,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GmailState> state)
    : Neuron<GmailState>(runtime, state), IGmail
{
    public Task<Accepted<GmailConnection>> Connect(ConnectGmailAccount command) => ExecuteCommandAsync(
        Descriptor("connect"), command, GmailJson.Default.ConnectGmailAccount, GmailJson.Default.AcceptedGmailConnection, arguments =>
        {
            GmailTokenRefresh.ValidateToken(arguments.AccessToken);
            if (arguments.RefreshToken is not null)
            {
                GmailTokenRefresh.ValidateToken(arguments.RefreshToken);
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Subject);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Email);
            if (arguments.Subject.Length > 256 || arguments.Email.Length > 320)
            {
                throw new GmailUnavailableException("Google identity validation was incomplete.");
            }
            var grants = GmailTokenRefresh.ParseScopes(arguments.GrantedScopes);
            if (!grants.Contains("openid") || !grants.Contains(GmailOAuthConfiguration.ReadScope)
                || !(grants.Contains("email") || grants.Contains("https://www.googleapis.com/auth/userinfo.email")))
            {
                throw new GmailUnavailableException("Google did not grant all required Gmail and identity scopes.");
            }
            var expiry = GmailTokenRefresh.Expiry(arguments.ExpiresInSeconds, TimeProvider);
            var receipt = new GmailConnection(true, arguments.Email, grants.Contains(GmailOAuthConfiguration.ComposeScope), expiry);
            var work = Schedule(CreateSignal(GmailSignals.GmailConnected, arguments, GmailJson.Default.ConnectGmailAccount));
            return new Accepted<GmailConnection>(receipt, work);
        });

    public Task<Accepted<GmailConnection>> Disconnect(DisconnectGmail command) => ExecuteCommandAsync(
        Descriptor("disconnect"), command, GmailJson.Default.DisconnectGmail, GmailJson.Default.AcceptedGmailConnection, arguments =>
        {
            var work = Schedule(CreateSignal(GmailSignals.GmailDisconnected, new GmailDisconnected(), GmailJson.Default.GmailDisconnected));
            return new Accepted<GmailConnection>(new(false, null, false, null), work);
        });

    public Task<Accepted<GmailDraftPreview>> PrepareDraft(PrepareGmailDraft command) => ExecuteCommandAsync(
        Descriptor("prepare-draft"), command, GmailJson.Default.PrepareGmailDraft, GmailJson.Default.AcceptedGmailDraftPreview, arguments =>
        {
            var connection = RequireConnection(compose: true);
            GmailContent.ValidateArguments("create_draft", DraftArguments(arguments.To, arguments.Cc, arguments.Bcc, arguments.Subject, arguments.Body));
            var preview = new GmailDraftPreview(arguments.Id.ToString(), "", arguments.To, arguments.Cc, arguments.Bcc,
                arguments.Subject, arguments.Body, TimeProvider.GetUtcNow().AddMinutes(10));
            var work = Schedule(CreateSignal(GmailSignals.GmailDraftRequested,
                new GmailDraftRequested(preview, connection.Subject!), GmailJson.Default.GmailDraftRequested));
            return new Accepted<GmailDraftPreview>(preview, work);
        });

    public Task<Accepted<GmailDraftPreview>> ConfirmDraft(ConfirmGmailDraft command) => ExecuteCommandAsync(
        Descriptor("confirm-draft"), command, GmailJson.Default.ConfirmGmailDraft, GmailJson.Default.AcceptedGmailDraftPreview, arguments =>
        {
            var preview = RequireConnection(compose: true).PendingDraft;
            if (preview is null || preview.PreviewId != arguments.PreviewId || preview.ExpiresAt <= TimeProvider.GetUtcNow()
                || string.IsNullOrEmpty(preview.ToolSchemaHash) || preview.ToolSchemaHash != arguments.ToolSchemaHash)
            {
                throw new GmailUnavailableException("The Gmail preview expired or changed. Prepare a fresh preview.");
            }
            var work = Schedule(CreateSignal(GmailSignals.GmailDraftConfirmed,
                new GmailDraftConfirmed(preview), GmailJson.Default.GmailDraftConfirmed));
            return new Accepted<GmailDraftPreview>(preview, work);
        });

    public Task<GmailConnection> ReadConnection() => Task.FromResult(Connection());

    public Task<GmailContentRead> SearchThreads(SearchGmailThreads query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ReadAsync("search_threads", new Dictionary<string, object?>
        {
            ["query"] = query.Query,
            ["pageSize"] = query.PageSize,
            ["pageToken"] = query.PageToken,
        }, cancellationToken);
    }

    public Task<GmailContentRead> ReadThread(ReadGmailThread query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ReadAsync("get_thread", new Dictionary<string, object?>
        {
            ["threadId"] = query.ThreadId,
            ["messageFormat"] = query.MessageFormat,
        }, cancellationToken);
    }

    public Task<GmailContentRead> ReadLabels(CancellationToken cancellationToken = default)
        => ReadAsync("list_labels", new Dictionary<string, object?>(), cancellationToken);

    private async Task<GmailContentRead> ReadAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        if (connection.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            throw new GmailNotConnectedException("The Gmail access token expired. Reconnect Gmail.");
        }
        return new(await provider.InvokeAsync(tool, arguments, connection.AccessToken!, cancellationToken).ConfigureAwait(true));
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case GmailSignals.GmailConnected:
                var account = JsonSerializer.Deserialize(delivery.Signal.Body, GmailJson.Default.ConnectGmailAccount)!;
                var grants = GmailTokenRefresh.ParseScopes(account.GrantedScopes);
                await SaveAsync(new GmailState(account.Subject, account.Email, account.AccessToken,
                    account.RefreshToken ?? (State?.Subject == account.Subject ? State.RefreshToken : null),
                    account.GrantedScopes, delivery.Timestamp.AddSeconds(account.ExpiresInSeconds),
                    grants.Contains(GmailOAuthConfiguration.ComposeScope)), cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(GmailSignals.GmailConnected, new GmailConnected(Connection()),
                    GmailJson.Default.GmailConnected), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case GmailSignals.GmailDisconnected:
                await SaveAsync(new GmailState(), cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(GmailSignals.GmailDisconnected, new GmailDisconnected(),
                    GmailJson.Default.GmailDisconnected), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case GmailSignals.GmailDraftRequested:
                var requested = JsonSerializer.Deserialize(delivery.Signal.Body, GmailJson.Default.GmailDraftRequested)!;
                if (RequireConnection(compose: true).Subject != requested.AccountSubject)
                {
                    return;
                }
                var hash = await ReadDraftSchemaAsync(cancellationToken).ConfigureAwait(true);
                var preview = requested.Preview with { ToolSchemaHash = hash };
                await SaveAsync(State! with { PendingDraft = preview }, cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(GmailSignals.GmailDraftPrepared, new GmailDraftPrepared(preview),
                    GmailJson.Default.GmailDraftPrepared), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case GmailSignals.GmailDraftConfirmed:
                await CreateDraftAsync(JsonSerializer.Deserialize(delivery.Signal.Body, GmailJson.Default.GmailDraftConfirmed)!.Preview, cancellationToken).ConfigureAwait(true);
                break;
        }
    }

    private async Task CreateDraftAsync(GmailDraftPreview confirmed, CancellationToken cancellationToken)
    {
        var preview = State?.PendingDraft;
        if (preview is null || preview.PreviewId != confirmed.PreviewId || preview.ToolSchemaHash != confirmed.ToolSchemaHash)
        {
            await FireUncertainAsync(confirmed, cancellationToken).ConfigureAwait(true);
            return;
        }
        // Consume durably before provider I/O so a crash cannot retry the same preview.
        await SaveAsync(State! with { PendingDraft = null }, cancellationToken).ConfigureAwait(true);
        GmailDraftPreview created;
        try
        {
            RequireConnection(compose: true);
            if (preview.ExpiresAt <= TimeProvider.GetUtcNow()
                || await ReadDraftSchemaAsync(cancellationToken).ConfigureAwait(true) != preview.ToolSchemaHash)
            {
                throw new GmailUnavailableException("The Gmail preview expired or its schema changed.");
            }
            var result = await provider.InvokeAsync("create_draft",
                DraftArguments(preview.To, preview.Cc, preview.Bcc, preview.Subject, preview.Body),
                State!.AccessToken!, cancellationToken).ConfigureAwait(true);
            var draftId = result.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(draftId))
            {
                throw new GmailUnavailableException("Gmail did not confirm the draft id.");
            }
            created = preview with { DraftId = draftId };
        }
        catch (Exception)
        {
            await FireUncertainAsync(preview, cancellationToken).ConfigureAwait(true);
            return;
        }
        await FireAsync(CreateSignal(GmailSignals.GmailDraftCreated, new GmailDraftCreated(created),
            GmailJson.Default.GmailDraftCreated), cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    private Task FireUncertainAsync(GmailDraftPreview preview, CancellationToken cancellationToken)
        => FireAsync(CreateSignal(GmailSignals.GmailDraftUncertain, new GmailDraftUncertain(preview.PreviewId),
            GmailJson.Default.GmailDraftUncertain), cancellationToken: cancellationToken);

    private async Task<string> ReadDraftSchemaAsync(CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        if (connection.ExpiresAt <= TimeProvider.GetUtcNow().AddSeconds(30))
        {
            try
            {
                connection = await tokenRefresh.RefreshAsync(connection, TimeProvider, cancellationToken).ConfigureAwait(true);
                await SaveAsync(connection, cancellationToken).ConfigureAwait(true);
            }
            catch (GmailNotConnectedException)
            {
                await SaveAsync(new GmailState(), cancellationToken).ConfigureAwait(true);
                throw;
            }
        }
        return await provider.ReadToolSchemaHashAsync("create_draft",
            connection.AccessToken!, cancellationToken).ConfigureAwait(true);
    }

    private static Signal CreateSignal<T>(string type, T body, JsonTypeInfo<T> json)
        => Signal.Create(type, JsonSerializer.Serialize(body, json));

    private GmailState RequireConnection(bool compose = false)
    {
        if (State is not { AccessToken: not null, Subject: not null, ExpiresAt: not null } connection)
        {
            throw new GmailNotConnectedException();
        }
        if (compose && !connection.CanCompose)
        {
            throw new GmailNotConnectedException("Reconnect Gmail with compose scope before preparing a draft.");
        }
        return connection;
    }

    private GmailConnection Connection()
        => State is { AccessToken: not null } connection
            ? new(true, connection.Email, connection.CanCompose, connection.ExpiresAt)
            : new(false, null, false, null);

    private static Dictionary<string, object?> DraftArguments(IReadOnlyList<string> to, IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc, string subject, string body)
        => new()
        {
            ["to"] = to.ToArray(),
            ["cc"] = cc.ToArray(),
            ["bcc"] = bcc.ToArray(),
            ["subject"] = subject,
            ["body"] = body,
        };
}
