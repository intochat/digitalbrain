using System.Text;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Salesforce.Signals;
using DigitalBrain.Sdk;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Salesforce;

[GrainType("salesforce")]
internal sealed class SalesforceNeuron(
    ISalesforceProvider provider,
    SalesforceTokenRefresh tokenRefresh,
    TokenHandoff handoff,
    SalesforceWriteAccess writeAccess,
    TimeProvider time,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SalesforceState> state)
    : Neuron, ISalesforce
{
    private const int MaxWriteArgumentsBytes = 24 * 1024;
    private static readonly JsonSerializerOptions ArgumentJson = new(JsonSerializerDefaults.Web);

    public async Task<SalesforceConnection> Connect(ConnectSalesforceAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!Uri.TryCreate(account.InstanceUrl, UriKind.Absolute, out var instanceUrl) || instanceUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new SalesforceUnavailableException("Salesforce did not issue a valid HTTPS instance URL.");
        }

        _ = SalesforceTokenRefresh.Expiry(account.ExpiresInSeconds, time);
        if (!handoff.TryPeek(account.Nonce, out var tokens))
        {
            await RejectAsync(new TokenHandoffExpiredException().Message);
            throw new SalesforceUnavailableException(new TokenHandoffExpiredException().Message);
        }

        try
        {
            SalesforceTokenRefresh.ValidateToken(tokens.AccessToken);
            if (tokens.RefreshToken is not null)
            {
                SalesforceTokenRefresh.ValidateToken(tokens.RefreshToken);
            }

            state.State = new SalesforceState(
                tokens.AccessToken,
                tokens.RefreshToken ?? (Current.InstanceUrl == account.InstanceUrl ? Current.RefreshToken : null),
                time.GetUtcNow().AddSeconds(account.ExpiresInSeconds),
                account.InstanceUrl);
            await state.WriteStateAsync();
            var connection = Connection();
            await PublishAsync(new SalesforceConnected(connection));
            handoff.Consume(account.Nonce);
            return connection;
        }
        catch (SalesforceUnavailableException error)
        {
            await RejectAsync(error.Message);
            throw;
        }
    }

    public async Task<SalesforceConnection> Refresh(RefreshSalesforceConnection command)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            state.State = await tokenRefresh.RefreshAsync(RequireConnection(), time, CancellationToken.None);
            await state.WriteStateAsync();
            var connection = Connection();
            await PublishAsync(new SalesforceRefreshed(connection));
            return connection;
        }
        catch (SalesforceNotConnectedException error)
        {
            state.State = new SalesforceState();
            await state.WriteStateAsync();
            await RejectAsync(error.Message);
            throw;
        }
        catch (SalesforceUnavailableException error)
        {
            await RejectAsync(error.Message);
            throw;
        }
    }

    public async Task<SalesforceConnection> Disconnect(DisconnectSalesforce command)
    {
        ArgumentNullException.ThrowIfNull(command);
        state.State = new SalesforceState();
        await state.WriteStateAsync();
        await PublishAsync(new SalesforceDisconnected());
        return new SalesforceConnection(false, null, null);
    }

    public async Task<SalesforceWritePreview> PrepareWrite(PrepareSalesforceWrite command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var connection = RequireConnection();
        if (command.Tool is not ("createRecord" or "updateRecord"))
        {
            throw new SalesforceUnavailableException("This Salesforce operation is not admitted.");
        }

        if (Encoding.UTF8.GetByteCount(command.Arguments) > MaxWriteArgumentsBytes)
        {
            throw new SalesforceUnavailableException("The complete Salesforce change must fit within 24 KiB.");
        }

        try
        {
            using var document = JsonDocument.Parse(command.Arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new SalesforceUnavailableException("Salesforce write arguments must be a JSON object.");
            }
        }
        catch (JsonException)
        {
            throw new SalesforceUnavailableException("Salesforce write arguments must be a JSON object.");
        }

        var schema = await ReadWriteSchemaAsync(command.Tool, CancellationToken.None);
        if (schema.Reason is { } reason)
        {
            state.State = schema.Connection;
            await state.WriteStateAsync();
            await RejectAsync(reason);
            throw new SalesforceUnavailableException(reason);
        }

        var preview = new SalesforceWritePreview(
            Guid.NewGuid().ToString("n"), command.Tool, schema.SchemaHash!, command.Arguments, time.GetUtcNow().AddMinutes(10));
        state.State = schema.Connection with { PendingWrite = preview };
        await state.WriteStateAsync();
        await PublishAsync(new SalesforceWritePrepared(preview));
        return preview;
    }

    public async Task<SalesforceWritePreview> ConfirmWrite(ConfirmSalesforceWrite command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var preview = RequireConnection().PendingWrite;
        if (preview is null || preview.PreviewId != command.PreviewId || preview.ExpiresAt <= time.GetUtcNow()
            || string.IsNullOrEmpty(preview.ToolSchemaHash) || preview.ToolSchemaHash != command.ToolSchemaHash)
        {
            throw new SalesforceUnavailableException("The Salesforce preview expired or changed. Prepare a fresh preview.");
        }

        var schema = await ReadWriteSchemaAsync(preview.Tool, CancellationToken.None);
        if (schema.Reason is { } reason)
        {
            state.State = schema.Connection with { PendingWrite = null };
            await state.WriteStateAsync();
            await RejectAsync(reason);
            await PublishAsync(new SalesforceWriteUncertain(preview.PreviewId));
            return preview;
        }

        if (schema.SchemaHash != preview.ToolSchemaHash)
        {
            throw new SalesforceUnavailableException("The Salesforce preview schema changed. Prepare a fresh preview.");
        }

        state.State = schema.Connection with { PendingWrite = null, SubmittingWrite = preview };
        await state.WriteStateAsync();
        try
        {
            var recordId = await SubmitWriteAsync(preview, CancellationToken.None);
            return preview with { RecordId = recordId };
        }
        finally
        {
            state.State = Current with { SubmittingWrite = null };
            await state.WriteStateAsync();
        }
    }

    [ReadOnly]
    public Task<SalesforceConnection> ReadConnection() => Task.FromResult(Connection());

    [ReadOnly]
    public async Task<SalesforceQueryResult> Query(SoqlQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            SalesforceQueryGuard.Validate(query.Query);
        }
        catch (ArgumentException)
        {
            throw new SalesforceUnavailableException("Use one SELECT with an outer WHERE and positive LIMIT. Comments, multiple statements and locking queries are not allowed.");
        }

        var result = await ReadAsync("soqlQuery", JsonSerializer.SerializeToElement(query, ArgumentJson), cancellationToken).ConfigureAwait(true);
        return new SalesforceQueryResult(result.GetRawText(), result.GetProperty("totalSize").GetInt32());
    }

    [ReadOnly]
    public async Task<SalesforceSchema> ReadSchema(ReadSalesforceSchema query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var objectName = query.ObjectName;
        if (objectName is not null && (objectName.Length is < 1 or > 255 || !objectName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')))
        {
            throw new SalesforceUnavailableException("Use a Salesforce object API name.");
        }

        var arguments = objectName is null
            ? EmptyArguments()
            : JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["object-name"] = objectName });
        return new SalesforceSchema((await ReadAsync("getObjectSchema", arguments, cancellationToken).ConfigureAwait(true)).GetRawText());
    }

    [ReadOnly]
    public async Task<SalesforceUserInfo> ReadUserInfo(CancellationToken cancellationToken = default)
        => new((await ReadAsync("getUserInfo", EmptyArguments(), cancellationToken).ConfigureAwait(true)).GetRawText());

    private async Task<string?> SubmitWriteAsync(SalesforceWritePreview preview, CancellationToken cancellationToken)
    {
        try
        {
            var connection = RequireConnection();
            using var arguments = JsonDocument.Parse(preview.Arguments);
            var result = await provider.InvokeAsync(preview.Tool, arguments.RootElement, connection.AccessToken!, cancellationToken).ConfigureAwait(true);
            if (result.ValueKind != JsonValueKind.Object
                || result.TryGetProperty("id", out var record) && record.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                throw new SalesforceUnavailableException("Salesforce returned an invalid response shape.");
            }

            if (result.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True)
            {
                await PublishAsync(new SalesforceWriteFailed(preview.PreviewId));
                return null;
            }

            var recordId = result.TryGetProperty("id", out var id) ? id.GetString() : null;
            await PublishAsync(new RecordWritten(preview with { RecordId = recordId }));
            return recordId;
        }
        catch (SalesforceNotConnectedException error)
        {
            await RejectAsync(error.Message);
            await PublishAsync(new SalesforceWriteUncertain(preview.PreviewId));
            return null;
        }
        catch (SalesforceUnavailableException)
        {
            await PublishAsync(new SalesforceWriteUncertain(preview.PreviewId));
            return null;
        }
    }

    private async Task<(SalesforceState Connection, string? SchemaHash, string? Reason)> ReadWriteSchemaAsync(string tool, CancellationToken cancellationToken)
    {
        try
        {
            return await writeAccess.ReadAsync(tool, RequireConnection(), time, cancellationToken).ConfigureAwait(true);
        }
        catch (SalesforceNotConnectedException error)
        {
            return (new SalesforceState(), null, error.Message);
        }
        catch (SalesforceUnavailableException error)
        {
            return (Current, null, error.Message);
        }
    }

    private async Task<JsonElement> ReadAsync(string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        if (connection.ExpiresAt <= time.GetUtcNow())
        {
            throw new SalesforceNotConnectedException("The Salesforce access token expired. Refresh the connection.");
        }

        return await provider.InvokeAsync(tool, arguments, connection.AccessToken!, cancellationToken).ConfigureAwait(true);
    }

    private Task RejectAsync(string reason) => PublishAsync(new SalesforceConnectionRejected(reason));

    private SalesforceState RequireConnection()
    {
        if (Current is not { AccessToken: not null, InstanceUrl: not null, ExpiresAt: not null } connection)
        {
            throw new SalesforceNotConnectedException();
        }

        return connection;
    }

    private SalesforceConnection Connection() => ToConnection(Current);

    private static SalesforceConnection ToConnection(SalesforceState connection)
        => connection is { AccessToken: not null }
            ? new SalesforceConnection(true, connection.InstanceUrl, connection.ExpiresAt)
            : new SalesforceConnection(false, null, null);

    private SalesforceState Current => state.State ?? new SalesforceState();

    private static JsonElement EmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}