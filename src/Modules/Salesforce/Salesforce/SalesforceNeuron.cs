using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
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
    public Task<Accepted<SalesforceConnection>> Connect(ConnectSalesforceAccount command) => ExecuteCommandAsync(
        Descriptor("connect"), command, SalesforceJson.Default.ConnectSalesforceAccount, SalesforceJson.Default.AcceptedSalesforceConnection, arguments =>
        {
            if (!Uri.TryCreate(arguments.InstanceUrl, UriKind.Absolute, out var instanceUrl) || instanceUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new SalesforceUnavailableException("Salesforce did not issue a valid HTTPS instance URL.");
            }
            var expiry = SalesforceTokenRefresh.Expiry(arguments.ExpiresInSeconds, TimeProvider);
            var receipt = new SalesforceConnection(true, arguments.InstanceUrl, expiry);
            var work = Schedule(CreateSignal(SalesforceSignals.SalesforceConnectionRequested, arguments, SalesforceJson.Default.ConnectSalesforceAccount));
            return new Accepted<SalesforceConnection>(receipt, work);
        });

    public Task<Accepted<SalesforceConnection>> Refresh(RefreshSalesforceConnection command) => ExecuteCommandAsync(
        Descriptor("refresh"), command, SalesforceJson.Default.RefreshSalesforceConnection, SalesforceJson.Default.AcceptedSalesforceConnection, arguments =>
        {
            if (RequireConnection().RefreshToken is null)
            {
                throw new SalesforceNotConnectedException();
            }
            var work = Schedule(CreateSignal(SalesforceSignals.SalesforceRefreshRequested, arguments, SalesforceJson.Default.RefreshSalesforceConnection));
            return new Accepted<SalesforceConnection>(Connection(), work);
        });

    public Task<Accepted<SalesforceConnection>> Disconnect(DisconnectSalesforce command) => ExecuteCommandAsync(
        Descriptor("disconnect"), command, SalesforceJson.Default.DisconnectSalesforce, SalesforceJson.Default.AcceptedSalesforceConnection, arguments =>
        {
            var work = Schedule(CreateSignal(SalesforceSignals.SalesforceDisconnectionRequested, arguments, SalesforceJson.Default.DisconnectSalesforce));
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
            var work = Schedule(CreateSignal(SalesforceSignals.SalesforceWriteRequested,
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
            var work = Schedule(CreateSignal(SalesforceSignals.SalesforceWriteConfirmed,
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
        switch (delivery.Signal.Type)
        {
            case SalesforceSignals.SalesforceConnectionRequested:
                var account = JsonSerializer.Deserialize(delivery.Signal.Body, SalesforceJson.Default.ConnectSalesforceAccount)!;
                if (!handoff.TryRedeem(account.Nonce, out var tokens))
                {
                    await RejectConnectionAsync(new TokenHandoffExpiredException().Message, cancellationToken).ConfigureAwait(true);
                    return;
                }
                try
                {
                    SalesforceTokenRefresh.ValidateToken(tokens.AccessToken);
                    if (tokens.RefreshToken is not null)
                    {
                        SalesforceTokenRefresh.ValidateToken(tokens.RefreshToken);
                    }
                }
                catch (SalesforceUnavailableException error)
                {
                    await RejectConnectionAsync(error.Message, cancellationToken).ConfigureAwait(true);
                    return;
                }
                await SaveAsync(new SalesforceState(tokens.AccessToken,
                    tokens.RefreshToken ?? (State?.InstanceUrl == account.InstanceUrl ? State.RefreshToken : null),
                    delivery.Timestamp.AddSeconds(account.ExpiresInSeconds), account.InstanceUrl), cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(SalesforceSignals.SalesforceConnected, new SalesforceConnected(Connection()),
                    SalesforceJson.Default.SalesforceConnected), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case SalesforceSignals.SalesforceRefreshRequested:
                SalesforceState refreshed;
                try
                {
                    refreshed = await tokenRefresh.RefreshAsync(RequireConnection(), TimeProvider, cancellationToken).ConfigureAwait(true);
                }
                catch (SalesforceNotConnectedException error)
                {
                    await SaveAsync(new SalesforceState(), cancellationToken).ConfigureAwait(true);
                    await RejectConnectionAsync(error.Message, cancellationToken).ConfigureAwait(true);
                    return;
                }
                catch (SalesforceUnavailableException error)
                {
                    await RejectConnectionAsync(error.Message, cancellationToken).ConfigureAwait(true);
                    return;
                }
                await SaveAsync(refreshed, cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(SalesforceSignals.SalesforceRefreshed, new SalesforceRefreshed(Connection()),
                    SalesforceJson.Default.SalesforceRefreshed), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case SalesforceSignals.SalesforceDisconnectionRequested:
                await SaveAsync(new SalesforceState(), cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(SalesforceSignals.SalesforceDisconnected, new SalesforceDisconnected(),
                    SalesforceJson.Default.SalesforceDisconnected), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case SalesforceSignals.SalesforceWriteRequested:
                var requested = JsonSerializer.Deserialize(delivery.Signal.Body, SalesforceJson.Default.SalesforceWriteRequested)!;
                string hash;
                try
                {
                    if (RequireConnection().InstanceUrl != requested.InstanceUrl)
                    {
                        throw new SalesforceNotConnectedException("The Salesforce account changed. Prepare a fresh preview.");
                    }
                    hash = await ReadWriteSchemaAsync(requested.Preview.Tool, cancellationToken).ConfigureAwait(true);
                }
                catch (Exception error) when (error is SalesforceNotConnectedException or SalesforceUnavailableException)
                {
                    await RejectConnectionAsync(error.Message, cancellationToken).ConfigureAwait(true);
                    return;
                }
                var preview = requested.Preview with { ToolSchemaHash = hash, ExpiresAt = TimeProvider.GetUtcNow().AddMinutes(10) };
                await SaveAsync(State! with { PendingWrite = preview }, cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(SalesforceSignals.SalesforceWritePrepared, new SalesforceWritePrepared(preview),
                    SalesforceJson.Default.SalesforceWritePrepared), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case SalesforceSignals.SalesforceWriteConfirmed:
                await SubmitWriteAsync(JsonSerializer.Deserialize(delivery.Signal.Body, SalesforceJson.Default.SalesforceWriteConfirmed)!, cancellationToken).ConfigureAwait(true);
                break;
        }
    }

    private async Task SubmitWriteAsync(SalesforceWriteConfirmed confirmed, CancellationToken cancellationToken)
    {
        var preview = State?.PendingWrite;
        if (preview is null || preview.PreviewId != confirmed.PreviewId || preview.ToolSchemaHash != confirmed.ToolSchemaHash)
        {
            await FireUncertainAsync(confirmed.PreviewId, cancellationToken).ConfigureAwait(true);
            return;
        }
        // Consume durably before provider I/O so a crash cannot retry the same preview.
        await SaveAsync(State! with { PendingWrite = null }, cancellationToken).ConfigureAwait(true);
        Signal outcome;
        try
        {
            RequireConnection();
            if (preview.ExpiresAt <= TimeProvider.GetUtcNow()
                || await ReadWriteSchemaAsync(preview.Tool, cancellationToken).ConfigureAwait(true) != preview.ToolSchemaHash)
            {
                throw new SalesforceUnavailableException("The Salesforce preview expired or its schema changed.");
            }
            using var arguments = JsonDocument.Parse(preview.Arguments);
            var result = await provider.InvokeAsync(preview.Tool, arguments.RootElement,
                State!.AccessToken!, cancellationToken).ConfigureAwait(true);
            if (result.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True)
            {
                outcome = CreateSignal(SalesforceSignals.SalesforceWriteFailed, new SalesforceWriteFailed(preview.PreviewId),
                    SalesforceJson.Default.SalesforceWriteFailed);
            }
            else
            {
                var recordId = result.TryGetProperty("id", out var id) ? id.GetString() : null;
                outcome = CreateSignal(SalesforceSignals.RecordWritten, new RecordWritten(preview with { RecordId = recordId }),
                    SalesforceJson.Default.RecordWritten);
            }
        }
        catch (SalesforceNotConnectedException error)
        {
            await RejectConnectionAsync(error.Message, cancellationToken).ConfigureAwait(true);
            await FireUncertainAsync(preview.PreviewId, cancellationToken).ConfigureAwait(true);
            return;
        }
        catch (Exception)
        {
            await FireUncertainAsync(preview.PreviewId, cancellationToken).ConfigureAwait(true);
            return;
        }
        await FireAsync(outcome, cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    private Task FireUncertainAsync(string previewId, CancellationToken cancellationToken)
        => FireAsync(CreateSignal(SalesforceSignals.SalesforceWriteUncertain, new SalesforceWriteUncertain(previewId),
            SalesforceJson.Default.SalesforceWriteUncertain), cancellationToken: cancellationToken);

    private async Task<string> ReadWriteSchemaAsync(string tool, CancellationToken cancellationToken)
    {
        try
        {
            var access = await writeAccess.ReadAsync(tool, RequireConnection(), TimeProvider, cancellationToken).ConfigureAwait(true);
            await SaveAsync(access.Connection, cancellationToken).ConfigureAwait(true);
            return access.SchemaHash;
        }
        catch (SalesforceNotConnectedException)
        {
            await SaveAsync(new SalesforceState(), cancellationToken).ConfigureAwait(true);
            throw;
        }
    }

    private Task RejectConnectionAsync(string reason, CancellationToken cancellationToken)
        => FireAsync(CreateSignal(SalesforceSignals.SalesforceConnectionRejected, new SalesforceConnectionRejected(reason),
            SalesforceJson.Default.SalesforceConnectionRejected), cancellationToken: cancellationToken);

    private static Signal CreateSignal<T>(string type, T body, JsonTypeInfo<T> json)
        => Signal.Create(type, JsonSerializer.Serialize(body, json));

    private SalesforceState RequireConnection()
    {
        if (State is not { AccessToken: not null, InstanceUrl: not null, ExpiresAt: not null } connection)
        {
            throw new SalesforceNotConnectedException();
        }
        return connection;
    }

    private SalesforceConnection Connection()
        => State is { AccessToken: not null } connection
            ? new(true, connection.InstanceUrl, connection.ExpiresAt)
            : new(false, null, null);

    private static JsonElement EmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
