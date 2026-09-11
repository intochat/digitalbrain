using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Google;

[GrainType("gmail")]
internal sealed class GmailNeuron(
    NeuronRuntime runtime,
    IGmailProvider provider,
    GmailTokenRefresh tokenRefresh,
    TokenHandoff handoff,
    GmailDraftAccess draftAccess,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<GmailState>> state)
    : Neuron<GmailState>(runtime, state), IGmail
{
    public Task<Accepted<SignalId>> Connect(ConnectGmailAccount command) => ExecuteCommandAsync(
        Descriptor("connect"), command, GmailJson.Default.ConnectGmailAccount, GmailJson.Default.AcceptedSignalId, arguments =>
        {
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
            _ = GmailTokenRefresh.Expiry(arguments.ExpiresInSeconds, TimeProvider);
            var work = Schedule(Signal.FromJson(GmailSignals.GmailConnectionRequested, arguments, GmailJson.Default.ConnectGmailAccount));
            return new Accepted<SignalId>(work, work);
        });

    public Task<Accepted<GmailConnection>> Refresh(RefreshGmailConnection command) => ExecuteCommandAsync(
        Descriptor("refresh"), command, GmailJson.Default.RefreshGmailConnection, GmailJson.Default.AcceptedGmailConnection, arguments =>
        {
            if (RequireConnection().RefreshToken is null)
            {
                throw new GmailNotConnectedException();
            }
            var work = Schedule(Signal.FromJson(GmailSignals.GmailRefreshRequested, arguments, GmailJson.Default.RefreshGmailConnection));
            return new Accepted<GmailConnection>(Connection(), work);
        });

    public Task<Accepted<GmailConnection>> Disconnect(DisconnectGmail command) => ExecuteCommandAsync(
        Descriptor("disconnect"), command, GmailJson.Default.DisconnectGmail, GmailJson.Default.AcceptedGmailConnection, arguments =>
        {
            var work = Schedule(Signal.FromJson(GmailSignals.GmailDisconnectionRequested, arguments, GmailJson.Default.DisconnectGmail));
            return new Accepted<GmailConnection>(new(false, null, false, null), work);
        });

    public Task<Accepted<GmailDraftPreview>> PrepareDraft(PrepareGmailDraft command) => ExecuteCommandAsync(
        Descriptor("prepare-draft"), command, GmailJson.Default.PrepareGmailDraft, GmailJson.Default.AcceptedGmailDraftPreview, arguments =>
        {
            var connection = RequireConnection(compose: true);
            GmailContent.ValidateArguments("create_draft", DraftArguments(arguments.To, arguments.Cc, arguments.Bcc, arguments.Subject, arguments.Body));
            var preview = new GmailDraftPreview(arguments.Id.ToString(), "", arguments.To, arguments.Cc, arguments.Bcc,
                arguments.Subject, arguments.Body, TimeProvider.GetUtcNow().AddMinutes(10));
            var work = Schedule(Signal.FromJson(GmailSignals.GmailDraftRequested,
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
            var work = Schedule(Signal.FromJson(GmailSignals.GmailDraftConfirmed,
                new GmailDraftConfirmed(preview.PreviewId, preview.ToolSchemaHash), GmailJson.Default.GmailDraftConfirmed));
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
            throw new GmailNotConnectedException("The Gmail access token expired. Refresh the connection.");
        }
        return new(await provider.InvokeAsync(tool, arguments, connection.AccessToken!, cancellationToken).ConfigureAwait(true));
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        GmailState? next = null;
        string? consumedNonce = null;
        switch (delivery.Signal.Type)
        {
            case GmailSignals.GmailConnectionRequested:
                if (Body(delivery, GmailJson.Default.ConnectGmailAccount) is not { } account)
                {
                    break;
                }
                if (!handoff.TryPeek(account.Nonce, out var tokens))
                {
                    next ??= State ?? new GmailState();
                    RejectConnection(new TokenHandoffExpiredException().Message);
                    break;
                }
                try
                {
                    GmailTokenRefresh.ValidateToken(tokens.AccessToken);
                    if (tokens.RefreshToken is not null)
                    {
                        GmailTokenRefresh.ValidateToken(tokens.RefreshToken);
                    }
                    next = new GmailState(account.Subject, account.Email, tokens.AccessToken,
                        tokens.RefreshToken ?? (State?.Subject == account.Subject ? State.RefreshToken : null),
                        account.GrantedScopes, delivery.Timestamp.AddSeconds(account.ExpiresInSeconds),
                        GmailTokenRefresh.ParseScopes(account.GrantedScopes).Contains(GmailOAuthConfiguration.ComposeScope));
                    Announce(Signal.FromJson(GmailSignals.GmailConnected, new GmailConnected(Connection(next)),
                        GmailJson.Default.GmailConnected));
                    consumedNonce = account.Nonce;
                }
                catch (GmailUnavailableException error)
                {
                    next ??= State ?? new GmailState();
                    RejectConnection(error.Message);
                }
                break;
            case GmailSignals.GmailRefreshRequested:
                try
                {
                    next = await tokenRefresh.RefreshAsync(RequireConnection(), TimeProvider, cancellationToken).ConfigureAwait(true);
                    Announce(Signal.FromJson(GmailSignals.GmailRefreshed, new GmailRefreshed(Connection(next)),
                        GmailJson.Default.GmailRefreshed));
                }
                catch (GmailNotConnectedException error)
                {
                    next = new GmailState();
                    RejectConnection(error.Message);
                }
                catch (GmailUnavailableException error)
                {
                    next ??= State ?? new GmailState();
                    RejectConnection(error.Message);
                }
                break;
            case GmailSignals.GmailDisconnectionRequested:
                next = new GmailState();
                Announce(Signal.FromJson(GmailSignals.GmailDisconnected, new GmailDisconnected(),
                    GmailJson.Default.GmailDisconnected));
                break;
            case GmailSignals.GmailDraftRequested:
                if (Body(delivery, GmailJson.Default.GmailDraftRequested) is not { } requested)
                {
                    break;
                }
                try
                {
                    if (RequireConnection(compose: true).Subject != requested.AccountSubject)
                    {
                        throw new GmailNotConnectedException("The Gmail account changed. Prepare a fresh preview.");
                    }
                    var schema = await ReadDraftSchemaAsync(cancellationToken).ConfigureAwait(true);
                    next = schema.Connection;
                    if (schema.Reason is { } reason)
                    {
                        RejectConnection(reason);
                        break;
                    }
                    var preview = requested.Preview with { ToolSchemaHash = schema.SchemaHash! };
                    next = next with { PendingDraft = preview };
                    Announce(Signal.FromJson(GmailSignals.GmailDraftPrepared, new GmailDraftPrepared(preview.PreviewId, preview.ToolSchemaHash),
                        GmailJson.Default.GmailDraftPrepared));
                }
                catch (Exception error) when (error is GmailNotConnectedException or GmailUnavailableException)
                {
                    next ??= State ?? new GmailState();
                    RejectConnection(error.Message);
                }
                break;
            case GmailSignals.GmailDraftConfirmed:
                if (Body(delivery, GmailJson.Default.GmailDraftConfirmed) is { } confirmed)
                {
                    next = await ConfirmDraftAsync(confirmed, cancellationToken).ConfigureAwait(true);
                }
                break;
            case GmailSignals.GmailDraftSubmitting:
                next = await SubmitDraftAsync(cancellationToken).ConfigureAwait(true);
                break;
        }
        if (next is not null)
        {
            await SaveAsync(next, cancellationToken).ConfigureAwait(true);
        }
        if (consumedNonce is not null)
        {
            handoff.Consume(consumedNonce);
        }
    }

    private async Task<GmailState> ConfirmDraftAsync(GmailDraftConfirmed confirmed, CancellationToken cancellationToken)
    {
        var next = State ?? new GmailState();
        var preview = next.PendingDraft;
        if (preview is null || preview.PreviewId != confirmed.PreviewId || preview.ToolSchemaHash != confirmed.ToolSchemaHash)
        {
            AnnounceUncertain(confirmed.PreviewId);
            return next;
        }
        try
        {
            RequireConnection(compose: true);
            if (preview.ExpiresAt <= TimeProvider.GetUtcNow())
            {
                throw new GmailUnavailableException("The Gmail preview expired. Prepare a fresh preview.");
            }
            var schema = await ReadDraftSchemaAsync(cancellationToken).ConfigureAwait(true);
            next = schema.Connection;
            if (schema.Reason is { } reason)
            {
                RejectConnection(reason);
                AnnounceUncertain(preview.PreviewId);
                return next with { PendingDraft = null };
            }
            if (schema.SchemaHash != preview.ToolSchemaHash)
            {
                throw new GmailUnavailableException("The Gmail preview schema changed. Prepare a fresh preview.");
            }
            Schedule(Signal.Create(GmailSignals.GmailDraftSubmitting, "{}"));
            return next with { PendingDraft = null, SubmittingDraft = preview };
        }
        catch (GmailNotConnectedException error)
        {
            RejectConnection(error.Message);
            AnnounceUncertain(preview.PreviewId);
        }
        catch (GmailUnavailableException)
        {
            AnnounceUncertain(preview.PreviewId);
        }
        return next with { PendingDraft = null };
    }

    private async Task<GmailState> SubmitDraftAsync(CancellationToken cancellationToken)
    {
        var next = State ?? new GmailState();
        if (next.SubmittingDraft is not { } preview)
        {
            return next;
        }
        try
        {
            RequireConnection(compose: true);
            var result = await provider.InvokeAsync("create_draft",
                DraftArguments(preview.To, preview.Cc, preview.Bcc, preview.Subject, preview.Body),
                State!.AccessToken!, cancellationToken).ConfigureAwait(true);
            if (result.ValueKind != System.Text.Json.JsonValueKind.Object
                || !result.TryGetProperty("id", out var id) || id.ValueKind != System.Text.Json.JsonValueKind.String
                || string.IsNullOrWhiteSpace(id.GetString()))
            {
                throw new GmailUnavailableException("Gmail did not confirm the draft id.");
            }
            var draftId = id.GetString()!;
            Announce(Signal.FromJson(GmailSignals.GmailDraftCreated, new GmailDraftCreated(preview.PreviewId, draftId),
                GmailJson.Default.GmailDraftCreated));
        }
        catch (GmailNotConnectedException error)
        {
            RejectConnection(error.Message);
            AnnounceUncertain(preview.PreviewId);
        }
        catch (GmailUnavailableException)
        {
            AnnounceUncertain(preview.PreviewId);
        }
        return next with { SubmittingDraft = null };
    }

    private void AnnounceUncertain(string previewId)
        => Announce(Signal.FromJson(GmailSignals.GmailDraftUncertain, new GmailDraftUncertain(previewId),
            GmailJson.Default.GmailDraftUncertain));

    private async Task<(GmailState Connection, string? SchemaHash, string? Reason)> ReadDraftSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await draftAccess.ReadAsync(RequireConnection(), TimeProvider, cancellationToken).ConfigureAwait(true);
        }
        catch (GmailNotConnectedException error)
        {
            return (new GmailState(), null, error.Message);
        }
        catch (GmailUnavailableException error)
        {
            return (State ?? new GmailState(), null, error.Message);
        }
    }

    private void RejectConnection(string reason)
        => Announce(Signal.FromJson(GmailSignals.GmailConnectionRejected, new GmailConnectionRejected(reason),
            GmailJson.Default.GmailConnectionRejected));

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

    private GmailConnection Connection() => Connection(State);

    private static GmailConnection Connection(GmailState? state)
        => state is { AccessToken: not null } connection
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
