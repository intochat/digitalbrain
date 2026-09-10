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
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SalesforceState> state)
    : Neuron<SalesforceState>(runtime, state), ISalesforce
{
    public Task<Accepted<SalesforceConnection>> Connect(ConnectSalesforceAccount command) => ExecuteCommandAsync(
        Descriptor("connect"), command, SalesforceJson.Default.ConnectSalesforceAccount, SalesforceJson.Default.AcceptedSalesforceConnection, arguments =>
        {
            SalesforceTokenRefresh.ValidateToken(arguments.AccessToken);
            if (arguments.RefreshToken is not null)
            {
                SalesforceTokenRefresh.ValidateToken(arguments.RefreshToken);
            }
            if (!Uri.TryCreate(arguments.InstanceUrl, UriKind.Absolute, out var instanceUrl) || instanceUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new SalesforceUnavailableException("Salesforce did not issue a valid HTTPS instance URL.");
            }
            var expiry = SalesforceTokenRefresh.Expiry(arguments.ExpiresInSeconds, TimeProvider);
            var receipt = new SalesforceConnection(true, arguments.InstanceUrl, expiry);
            var work = Schedule(CreateSignal(SalesforceSignals.SalesforceConnected, arguments, SalesforceJson.Default.ConnectSalesforceAccount));
            return new Accepted<SalesforceConnection>(receipt, work);
        });

    public Task<Accepted<SalesforceConnection>> Disconnect(DisconnectSalesforce command) => ExecuteCommandAsync(
        Descriptor("disconnect"), command, SalesforceJson.Default.DisconnectSalesforce, SalesforceJson.Default.AcceptedSalesforceConnection, arguments =>
        {
            var work = Schedule(CreateSignal(SalesforceSignals.SalesforceDisconnected, new SalesforceDisconnected(), SalesforceJson.Default.SalesforceDisconnected));
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
                new SalesforceWriteConfirmed(preview), SalesforceJson.Default.SalesforceWriteConfirmed));
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
        return new(result.GetProperty("records").Clone(), result.GetProperty("totalSize").GetInt32());
    }

    public async Task<SalesforceUserInfo> ReadUserInfo(CancellationToken cancellationToken = default)
        => new(await ReadAsync("getUserInfo", EmptyArguments(), cancellationToken).ConfigureAwait(true));

    private async Task<JsonElement> ReadAsync(string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        if (connection.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            throw new SalesforceNotConnectedException("The Salesforce access token expired. Reconnect Salesforce.");
        }
        return await provider.InvokeAsync(tool, arguments, connection.AccessToken!, cancellationToken).ConfigureAwait(true);
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case SalesforceSignals.SalesforceConnected:
                var account = JsonSerializer.Deserialize(delivery.Signal.Body, SalesforceJson.Default.ConnectSalesforceAccount)!;
                await SaveAsync(new SalesforceState(account.AccessToken,
                    account.RefreshToken ?? (State?.InstanceUrl == account.InstanceUrl ? State.RefreshToken : null),
                    delivery.Timestamp.AddSeconds(account.ExpiresInSeconds), account.InstanceUrl), cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(SalesforceSignals.SalesforceConnected, new SalesforceConnected(Connection()),
                    SalesforceJson.Default.SalesforceConnected), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case SalesforceSignals.SalesforceDisconnected:
                await SaveAsync(new SalesforceState(), cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(SalesforceSignals.SalesforceDisconnected, new SalesforceDisconnected(),
                    SalesforceJson.Default.SalesforceDisconnected), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case SalesforceSignals.SalesforceWriteRequested:
                var requested = JsonSerializer.Deserialize(delivery.Signal.Body, SalesforceJson.Default.SalesforceWriteRequested)!;
                if (RequireConnection().InstanceUrl != requested.InstanceUrl)
                {
                    return;
                }
                var hash = await ReadWriteSchemaAsync(requested.Preview.Tool, cancellationToken).ConfigureAwait(true);
                var preview = requested.Preview with { ToolSchemaHash = hash, ExpiresAt = TimeProvider.GetUtcNow().AddMinutes(10) };
                await SaveAsync(State! with { PendingWrite = preview }, cancellationToken).ConfigureAwait(true);
                await FireAsync(CreateSignal(SalesforceSignals.SalesforceWritePrepared, new SalesforceWritePrepared(preview),
                    SalesforceJson.Default.SalesforceWritePrepared), cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case SalesforceSignals.SalesforceWriteConfirmed:
                await SubmitWriteAsync(JsonSerializer.Deserialize(delivery.Signal.Body, SalesforceJson.Default.SalesforceWriteConfirmed)!.Preview, cancellationToken).ConfigureAwait(true);
                break;
        }
    }

    private async Task SubmitWriteAsync(SalesforceWritePreview confirmed, CancellationToken cancellationToken)
    {
        var preview = State?.PendingWrite;
        if (preview is null || preview.PreviewId != confirmed.PreviewId || preview.ToolSchemaHash != confirmed.ToolSchemaHash)
        {
            await FireUncertainAsync(confirmed, cancellationToken).ConfigureAwait(true);
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
        catch (Exception)
        {
            await FireUncertainAsync(preview, cancellationToken).ConfigureAwait(true);
            return;
        }
        await FireAsync(outcome, cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    private Task FireUncertainAsync(SalesforceWritePreview preview, CancellationToken cancellationToken)
        => FireAsync(CreateSignal(SalesforceSignals.SalesforceWriteUncertain, new SalesforceWriteUncertain(preview.PreviewId),
            SalesforceJson.Default.SalesforceWriteUncertain), cancellationToken: cancellationToken);

    private async Task<string> ReadWriteSchemaAsync(string tool, CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        if (connection.ExpiresAt <= TimeProvider.GetUtcNow().AddSeconds(30))
        {
            try
            {
                connection = await tokenRefresh.RefreshAsync(connection, TimeProvider, cancellationToken).ConfigureAwait(true);
                await SaveAsync(connection, cancellationToken).ConfigureAwait(true);
            }
            catch (SalesforceNotConnectedException)
            {
                await SaveAsync(new SalesforceState(), cancellationToken).ConfigureAwait(true);
                throw;
            }
        }
        return await provider.ReadToolSchemaHashAsync(tool, connection.AccessToken!, cancellationToken).ConfigureAwait(true);
    }

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
