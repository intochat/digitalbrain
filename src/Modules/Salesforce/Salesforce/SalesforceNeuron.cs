using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Salesforce;

[GrainType("salesforce")]
internal sealed class SalesforceNeuron(
    NeuronRuntime runtime,
    ISalesforceProvider provider,
    SalesforceTokenRefresh tokenRefresh,
    TokenHandoff handoff,
    SalesforceWriteAccess writeAccess,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<SalesforceState>> state)
    : Neuron<SalesforceState>(runtime, state), ISalesforce
{
    public Task<Accepted<SignalId>> Connect(ConnectSalesforceAccount command) => ExecuteCommandAsync(
        Descriptor("connect"), command, SalesforceJson.Default.ConnectSalesforceAccount, SalesforceJson.Default.AcceptedSignalId, arguments =>
        {
            if (!Uri.TryCreate(arguments.InstanceUrl, UriKind.Absolute, out var instanceUrl) || instanceUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new SalesforceUnavailableException("Salesforce did not issue a valid HTTPS instance URL.");
            }
            _ = SalesforceTokenRefresh.Expiry(arguments.ExpiresInSeconds, TimeProvider);
            var work = Schedule(Signal.FromJson(SalesforceSignals.SalesforceConnectionRequested, arguments, SalesforceJson.Default.ConnectSalesforceAccount));
            return new Accepted<SignalId>(work, work);
        });

    public Task<Accepted<SalesforceConnection>> Refresh(RefreshSalesforceConnection command) => ExecuteCommandAsync(
        Descriptor("refresh"), command, SalesforceJson.Default.RefreshSalesforceConnection, SalesforceJson.Default.AcceptedSalesforceConnection, arguments =>
        {
            if (RequireConnection().RefreshToken is null)
            {
                throw new SalesforceNotConnectedException();
            }
            var work = Schedule(Signal.FromJson(SalesforceSignals.SalesforceRefreshRequested, arguments, SalesforceJson.Default.RefreshSalesforceConnection));
            return new Accepted<SalesforceConnection>(Connection(), work);
        });

    public Task<Accepted<SalesforceConnection>> Disconnect(DisconnectSalesforce command) => ExecuteCommandAsync(
        Descriptor("disconnect"), command, SalesforceJson.Default.DisconnectSalesforce, SalesforceJson.Default.AcceptedSalesforceConnection, arguments =>
        {
            var work = Schedule(Signal.FromJson(SalesforceSignals.SalesforceDisconnectionRequested, arguments, SalesforceJson.Default.DisconnectSalesforce));
            return new Accepted<SalesforceConnection>(new(false, null, null), work);
        });

    public Task<Accepted<SalesforceWritePreview>> PrepareWrite(PrepareSalesforceWrite command) => ExecuteCommandAsync(
        Descriptor("prepare-write"), command, SalesforceJson.Default.PrepareSalesforceWrite, SalesforceJson.Default.AcceptedSalesforceWritePreview, arguments =>
        {
            var connection = RequireConnection();
            if (arguments.Tool is not ("createRecord" or "updateRecord"))
            {
                throw new SalesforceUnavailableException("This Salesforce operation is not admitted.");
            }
            if (System.Text.Encoding.UTF8.GetByteCount(arguments.Arguments) > 24 * 1024)
            {
                throw new SalesforceUnavailableException("The complete Salesforce change must fit within 24 KiB.");
            }
            try
            {
                using var document = JsonDocument.Parse(arguments.Arguments);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new SalesforceUnavailableException("Salesforce write arguments must be a JSON object.");
                }
            }
            catch (JsonException)
            {
                throw new SalesforceUnavailableException("Salesforce write arguments must be a JSON object.");
            }
            var preview = new SalesforceWritePreview(arguments.Id.ToString(), arguments.Tool, "", arguments.Arguments,
                TimeProvider.GetUtcNow().AddMinutes(10));
            var work = Schedule(Signal.FromJson(SalesforceSignals.SalesforceWriteRequested,
                new SalesforceWriteRequested(preview, connection.InstanceUrl!), SalesforceJson.Default.SalesforceWriteRequested));
            return new Accepted<SalesforceWritePreview>(preview, work);
        });

    public Task<Accepted<SalesforceWritePreview>> ConfirmWrite(ConfirmSalesforceWrite command) => ExecuteCommandAsync(
        Descriptor("confirm-write"), command, SalesforceJson.Default.ConfirmSalesforceWrite, SalesforceJson.Default.AcceptedSalesforceWritePreview, arguments =>
        {
            var preview = RequireConnection().PendingWrite;
            if (preview is null || preview.PreviewId != arguments.PreviewId || preview.ExpiresAt <= TimeProvider.GetUtcNow()
                || string.IsNullOrEmpty(preview.ToolSchemaHash) || preview.ToolSchemaHash != arguments.ToolSchemaHash)
            {
                throw new SalesforceUnavailableException("The Salesforce preview expired or changed. Prepare a fresh preview.");
            }
            var work = Schedule(Signal.FromJson(SalesforceSignals.SalesforceWriteConfirmed,
                new SalesforceWriteConfirmed(preview.PreviewId, preview.ToolSchemaHash), SalesforceJson.Default.SalesforceWriteConfirmed));
            return new Accepted<SalesforceWritePreview>(preview, work);
        });

    public Task<SalesforceConnection> ReadConnection() => Task.FromResult(Connection());

    public async Task<SalesforceQueryResult> Query(SoqlQuery query, CancellationToken cancellationToken = default)
    {
        try { SalesforceQueryGuard.Validate(query.Query); }
        catch (ArgumentException)
        {
            throw new SalesforceUnavailableException("Use one SELECT with an outer WHERE and positive LIMIT. Comments, multiple statements and locking queries are not allowed.");
        }
        var result = await ReadAsync("soqlQuery", JsonSerializer.SerializeToElement(query, SalesforceJson.Default.SoqlQuery), cancellationToken).ConfigureAwait(true);
        return new(result, result.GetProperty("totalSize").GetInt32());
    }

    public async Task<SalesforceUserInfo> ReadUserInfo(CancellationToken cancellationToken = default)
        => new(await ReadAsync("getUserInfo", EmptyArguments(), cancellationToken).ConfigureAwait(true));

    private async Task<JsonElement> ReadAsync(string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        if (connection.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            throw new SalesforceNotConnectedException("The Salesforce access token expired. Refresh the connection.");
        }
        return await provider.InvokeAsync(tool, arguments, connection.AccessToken!, cancellationToken).ConfigureAwait(true);
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        SalesforceState? next = null;
        string? consumedNonce = null;
        switch (delivery.Signal.Type)
        {
            case SalesforceSignals.SalesforceConnectionRequested:
                if (Body(delivery, SalesforceJson.Default.ConnectSalesforceAccount) is not { } account)
                {
                    break;
                }
                if (!handoff.TryPeek(account.Nonce, out var tokens))
                {
                    next ??= State ?? new SalesforceState();
                    RejectConnection(new TokenHandoffExpiredException().Message);
                    break;
                }
                try
                {
                    SalesforceTokenRefresh.ValidateToken(tokens.AccessToken);
                    if (tokens.RefreshToken is not null)
                    {
                        SalesforceTokenRefresh.ValidateToken(tokens.RefreshToken);
                    }
                    next = new SalesforceState(tokens.AccessToken,
                        tokens.RefreshToken ?? (State?.InstanceUrl == account.InstanceUrl ? State.RefreshToken : null),
                        delivery.Timestamp.AddSeconds(account.ExpiresInSeconds), account.InstanceUrl);
                    Announce(Signal.FromJson(SalesforceSignals.SalesforceConnected, new SalesforceConnected(Connection(next)),
                        SalesforceJson.Default.SalesforceConnected));
                    consumedNonce = account.Nonce;
                }
                catch (SalesforceUnavailableException error)
                {
                    next ??= State ?? new SalesforceState();
                    RejectConnection(error.Message);
                }
                break;
            case SalesforceSignals.SalesforceRefreshRequested:
                try
                {
                    next = await tokenRefresh.RefreshAsync(RequireConnection(), TimeProvider, cancellationToken).ConfigureAwait(true);
                    Announce(Signal.FromJson(SalesforceSignals.SalesforceRefreshed, new SalesforceRefreshed(Connection(next)),
                        SalesforceJson.Default.SalesforceRefreshed));
                }
                catch (SalesforceNotConnectedException error)
                {
                    next = new SalesforceState();
                    RejectConnection(error.Message);
                }
                catch (SalesforceUnavailableException error)
                {
                    next ??= State ?? new SalesforceState();
                    RejectConnection(error.Message);
                }
                break;
            case SalesforceSignals.SalesforceDisconnectionRequested:
                next = new SalesforceState();
                Announce(Signal.FromJson(SalesforceSignals.SalesforceDisconnected, new SalesforceDisconnected(),
                    SalesforceJson.Default.SalesforceDisconnected));
                break;
            case SalesforceSignals.SalesforceWriteRequested:
                if (Body(delivery, SalesforceJson.Default.SalesforceWriteRequested) is not { } requested)
                {
                    break;
                }
                try
                {
                    if (RequireConnection().InstanceUrl != requested.InstanceUrl)
                    {
                        throw new SalesforceNotConnectedException("The Salesforce account changed. Prepare a fresh preview.");
                    }
                    var schema = await ReadWriteSchemaAsync(requested.Preview.Tool, cancellationToken).ConfigureAwait(true);
                    next = schema.Connection;
                    if (schema.Reason is { } reason)
                    {
                        RejectConnection(reason);
                        break;
                    }
                    var preview = requested.Preview with { ToolSchemaHash = schema.SchemaHash!, ExpiresAt = TimeProvider.GetUtcNow().AddMinutes(10) };
                    next = next with { PendingWrite = preview };
                    Announce(Signal.FromJson(SalesforceSignals.SalesforceWritePrepared, new SalesforceWritePrepared(preview),
                        SalesforceJson.Default.SalesforceWritePrepared));
                }
                catch (Exception error) when (error is SalesforceNotConnectedException or SalesforceUnavailableException)
                {
                    next ??= State ?? new SalesforceState();
                    RejectConnection(error.Message);
                }
                break;
            case SalesforceSignals.SalesforceWriteConfirmed:
                if (Body(delivery, SalesforceJson.Default.SalesforceWriteConfirmed) is { } confirmed)
                {
                    next = await ConfirmWriteAsync(confirmed, cancellationToken).ConfigureAwait(true);
                }
                break;
            case SalesforceSignals.SalesforceWriteSubmitting:
                next = await SubmitWriteAsync(cancellationToken).ConfigureAwait(true);
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

    private async Task<SalesforceState> ConfirmWriteAsync(SalesforceWriteConfirmed confirmed, CancellationToken cancellationToken)
    {
        var next = State ?? new SalesforceState();
        var preview = next.PendingWrite;
        if (preview is null || preview.PreviewId != confirmed.PreviewId || preview.ToolSchemaHash != confirmed.ToolSchemaHash)
        {
            AnnounceUncertain(confirmed.PreviewId);
            return next;
        }
        try
        {
            RequireConnection();
            if (preview.ExpiresAt <= TimeProvider.GetUtcNow())
            {
                throw new SalesforceUnavailableException("The Salesforce preview expired. Prepare a fresh preview.");
            }
            var schema = await ReadWriteSchemaAsync(preview.Tool, cancellationToken).ConfigureAwait(true);
            next = schema.Connection;
            if (schema.Reason is { } reason)
            {
                RejectConnection(reason);
                AnnounceUncertain(preview.PreviewId);
                return next with { PendingWrite = null };
            }
            if (schema.SchemaHash != preview.ToolSchemaHash)
            {
                throw new SalesforceUnavailableException("The Salesforce preview schema changed. Prepare a fresh preview.");
            }
            Schedule(Signal.Create(SalesforceSignals.SalesforceWriteSubmitting, "{}"));
            return next with { PendingWrite = null, SubmittingWrite = preview };
        }
        catch (SalesforceNotConnectedException error)
        {
            RejectConnection(error.Message);
            AnnounceUncertain(preview.PreviewId);
        }
        catch (SalesforceUnavailableException)
        {
            AnnounceUncertain(preview.PreviewId);
        }
        return next with { PendingWrite = null };
    }

    private async Task<SalesforceState> SubmitWriteAsync(CancellationToken cancellationToken)
    {
        var next = State ?? new SalesforceState();
        if (next.SubmittingWrite is not { } preview)
        {
            return next;
        }
        try
        {
            RequireConnection();
            using var arguments = JsonDocument.Parse(preview.Arguments);
            var result = await provider.InvokeAsync(preview.Tool, arguments.RootElement,
                State!.AccessToken!, cancellationToken).ConfigureAwait(true);
            if (result.ValueKind != JsonValueKind.Object
                || result.TryGetProperty("id", out var record) && record.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                throw new SalesforceUnavailableException("Salesforce returned an invalid response shape.");
            }
            if (result.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True)
            {
                Announce(Signal.FromJson(SalesforceSignals.SalesforceWriteFailed, new SalesforceWriteFailed(preview.PreviewId),
                    SalesforceJson.Default.SalesforceWriteFailed));
            }
            else
            {
                var recordId = result.TryGetProperty("id", out var id) ? id.GetString() : null;
                Announce(Signal.FromJson(SalesforceSignals.RecordWritten, new RecordWritten(preview with { RecordId = recordId }),
                    SalesforceJson.Default.RecordWritten));
            }
        }
        catch (SalesforceNotConnectedException error)
        {
            RejectConnection(error.Message);
            AnnounceUncertain(preview.PreviewId);
        }
        catch (SalesforceUnavailableException)
        {
            AnnounceUncertain(preview.PreviewId);
        }
        return next with { SubmittingWrite = null };
    }

    private void AnnounceUncertain(string previewId)
        => Announce(Signal.FromJson(SalesforceSignals.SalesforceWriteUncertain, new SalesforceWriteUncertain(previewId),
            SalesforceJson.Default.SalesforceWriteUncertain));

    private async Task<(SalesforceState Connection, string? SchemaHash, string? Reason)> ReadWriteSchemaAsync(string tool, CancellationToken cancellationToken)
    {
        try
        {
            return await writeAccess.ReadAsync(tool, RequireConnection(), TimeProvider, cancellationToken).ConfigureAwait(true);
        }
        catch (SalesforceNotConnectedException error)
        {
            return (new SalesforceState(), null, error.Message);
        }
        catch (SalesforceUnavailableException error)
        {
            return (State ?? new SalesforceState(), null, error.Message);
        }
    }

    private void RejectConnection(string reason)
        => Announce(Signal.FromJson(SalesforceSignals.SalesforceConnectionRejected, new SalesforceConnectionRejected(reason),
            SalesforceJson.Default.SalesforceConnectionRejected));

    private SalesforceState RequireConnection()
    {
        if (State is not { AccessToken: not null, InstanceUrl: not null, ExpiresAt: not null } connection)
        {
            throw new SalesforceNotConnectedException();
        }
        return connection;
    }

    private SalesforceConnection Connection() => Connection(State);

    private static SalesforceConnection Connection(SalesforceState? state)
        => state is { AccessToken: not null } connection
            ? new(true, connection.InstanceUrl, connection.ExpiresAt)
            : new(false, null, null);

    private static JsonElement EmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
