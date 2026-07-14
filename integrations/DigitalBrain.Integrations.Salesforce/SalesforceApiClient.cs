using System.Globalization;
using System.Text;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Integrations.Salesforce.Contracts;
using StjSerializer = System.Text.Json.JsonSerializer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salesforce.Common.Models.Json;
using Salesforce.Force;
namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceApiClient(ForceClient client) : ISalesforceApiClient
{
    private const int MaximumResults = 200;
    private const int MaximumMutationValueLength = 4_096;
    private const int MaximumOriginalValueLength = 32_768;
    private const int MaximumPreparedUpdateLength = 64 * 1_024;
    public async Task<SalesforceRecord> ReadRecordAsync(DigitalBrain.Integrations.Salesforce.Contracts.SalesforceRecordReadRequest request, CancellationToken cancellationToken = default)
    {
        var objectName = ApiIdentifier(request.Record.ObjectName);
        var requestedFields = request.Fields.Select(ApiIdentifier).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var result = await client.QueryAsync<Dictionary<string, object?>>($"SELECT {string.Join(", ", requestedFields)} FROM {objectName} WHERE Id = '{request.Record.RecordId}' LIMIT 1")
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Records.Count != 1)
            throw new KeyNotFoundException("The requested Salesforce record is unavailable.");
        var record = result.Records[0];
        var fields = requestedFields.ToDictionary(
            field => field,
            field => StjSerializer.SerializeToElement(record.TryGetValue(field, out var value) ? Normalize(value) : null),
            StringComparer.Ordinal);
        return new SalesforceRecord(request.Record, fields);
    }
    private static string ApiIdentifier(string value)
    {
        if (value.Length is < 1 or > 255 || !char.IsAsciiLetter(value[0]) && value[0] != '_' ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("A valid Salesforce API identifier is required.", nameof(value));
        return value;
    }
    public async Task<string[]> ListAccountsAsync(int maxResults, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await client.QueryAsync<Dictionary<string, object?>>($"SELECT Id, Name FROM Account ORDER BY LastModifiedDate DESC LIMIT {Math.Clamp(maxResults, 1, 50)}").ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Records.Select(record => JsonConvert.SerializeObject(Normalize(record))).ToArray();
    }
    public async Task<SalesforceMutationPreviewResult> PreviewUpdateAsync(SalesforceUpdatePreviewRequest request, CancellationToken ct)
    {
        try
        {
            ValidatePreviewRequest(request);
            var schema = await ResolveObjectAsync(request.Entity, requireQueryable: true, requireSearchable: false, ct, requireUpdateable: true).ConfigureAwait(false);
            ValidateRecordId(request.RecordId, schema.KeyPrefix);
            var field = ResolveField(schema, request.Field, "update");
            if (!field.Updateable || field == schema.IdField)
                throw new SalesforceReadException(SalesforceReadFailure.AccessDenied, "That Salesforce field is not updateable for this connection.");
            var (_, desiredValue) = CompileMutationValue(field, request.NewValue);
            var originalValue = await ReadMutationValueAsync(schema, field, request.RecordId, ct).ConfigureAwait(false);
            if (originalValue is { Length: > MaximumOriginalValueLength })
                throw Invalid("The Salesforce field value is too large to prepare safely.");
            var document = new PreparedUpdateDocument(Version: 1, request.Entity.Label, schema.ApiName, request.RecordId, request.Field.Label, field.ApiName, field.Type, originalValue, desiredValue);
            var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document));
            if (payload.Length > MaximumPreparedUpdateLength)
                throw Invalid("The Salesforce update is too large to prepare safely.");
            return new SalesforceMutationPreviewResult(
                SalesforceMutationStatus.Prepared,
                originalValue,
                new SalesforcePreparedUpdate(payload),
                CanonicalDesiredValue: desiredValue,
                ResolvedEntityLabel: schema.Label,
                ResolvedFieldLabel: field.Label);
        }
        catch (SalesforceReadException ex)
        {
            return PreviewFailure(ex);
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            return new SalesforceMutationPreviewResult(SalesforceMutationStatus.Unavailable, SafeReason: "Salesforce updates are unavailable right now.");
        }
    }
    public async Task<SalesforceMutationApplyResult> ApplyUpdateAsync(SalesforcePreparedUpdate preparedUpdate, CancellationToken ct)
    {
        try
        {
            var document = ReadPreparedUpdate(preparedUpdate);
            var schema = await ResolveObjectAsync(new SalesforceSemanticEntity(document.EntityLabel), requireQueryable: true, requireSearchable: false, ct, requireUpdateable: true).ConfigureAwait(false);
            ValidateRecordId(document.RecordId, schema.KeyPrefix);
            var field = ResolveField(schema, new SalesforceSemanticField(document.FieldLabel), "update");
            if (!field.Updateable || field == schema.IdField)
                throw new SalesforceReadException(SalesforceReadFailure.AccessDenied, "That Salesforce field is not updateable for this connection.");
            if (!string.Equals(schema.ApiName, document.ObjectApiName, StringComparison.Ordinal) ||
                !string.Equals(field.ApiName, document.FieldApiName, StringComparison.Ordinal) ||
                !string.Equals(field.Type, document.FieldType, StringComparison.OrdinalIgnoreCase))
                throw Invalid("The prepared Salesforce update no longer matches provider metadata.");
            var (providerValue, desiredValue) = CompileMutationValue(field, document.DesiredValue);
            if (!string.Equals(desiredValue, document.DesiredValue, StringComparison.Ordinal))
                throw Invalid("The prepared Salesforce update is invalid.");
            var currentValue = await ReadMutationValueAsync(schema, field, document.RecordId, ct).ConfigureAwait(false);
            if (string.Equals(currentValue, desiredValue, StringComparison.Ordinal))
                return new SalesforceMutationApplyResult(SalesforceMutationStatus.AlreadyApplied);
            if (!string.Equals(currentValue, document.OriginalValue, StringComparison.Ordinal))
                return new SalesforceMutationApplyResult(SalesforceMutationStatus.Conflict, "The Salesforce record changed after this update was prepared.");
            ct.ThrowIfCancellationRequested();
            await client.UpdateAsync(schema.ApiName, document.RecordId, new Dictionary<string, object?> { [field.ApiName] = providerValue }).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var verifiedValue = await ReadMutationValueAsync(schema, field, document.RecordId, ct).ConfigureAwait(false);
            return string.Equals(verifiedValue, desiredValue, StringComparison.Ordinal)
                ? new SalesforceMutationApplyResult(SalesforceMutationStatus.Applied)
                : new SalesforceMutationApplyResult(SalesforceMutationStatus.VerificationFailed, "Salesforce did not confirm the requested update.");
        }
        catch (SalesforceReadException ex)
        {
            return ApplyFailure(ex);
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            return new SalesforceMutationApplyResult(SalesforceMutationStatus.Unavailable, "Salesforce updates are unavailable right now.");
        }
    }
    public async Task<SalesforceMutationVerificationResult> VerifyUpdateAsync(SalesforcePreparedUpdate preparedUpdate, CancellationToken ct)
    {
        try
        {
            var document = ReadPreparedUpdate(preparedUpdate);
            var schema = await ResolveObjectAsync(new SalesforceSemanticEntity(document.EntityLabel), requireQueryable: true, requireSearchable: false, ct, requireUpdateable: true).ConfigureAwait(false);
            ValidateRecordId(document.RecordId, schema.KeyPrefix);
            var field = ResolveField(schema, new SalesforceSemanticField(document.FieldLabel), "verification");
            if (!string.Equals(schema.ApiName, document.ObjectApiName, StringComparison.Ordinal) ||
                !string.Equals(field.ApiName, document.FieldApiName, StringComparison.Ordinal) ||
                !string.Equals(field.Type, document.FieldType, StringComparison.OrdinalIgnoreCase))
                return new SalesforceMutationVerificationResult(false, "The approved Salesforce update no longer matches provider metadata.");
            var currentValue = await ReadMutationValueAsync(schema, field, document.RecordId, ct).ConfigureAwait(false);
            return string.Equals(currentValue, document.DesiredValue, StringComparison.Ordinal)
                ? new SalesforceMutationVerificationResult(true)
                : new SalesforceMutationVerificationResult(false, "Salesforce did not confirm the requested update.");
        }
        catch (SalesforceReadException ex)
        {
            return new SalesforceMutationVerificationResult(false, ex.Message);
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            return new SalesforceMutationVerificationResult(false, "Salesforce verification is unavailable right now.");
        }
    }
    private async Task<string?> ReadMutationValueAsync(ObjectSchema schema, FieldSchema field, string recordId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await client.QueryAsync<Dictionary<string, object?>>($"SELECT {schema.IdField.ApiName}, {field.ApiName} FROM {schema.ApiName} WHERE {schema.IdField.ApiName} = '{recordId}' LIMIT 1").ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (result.Records.Count != 1)
            throw Invalid("The approved Salesforce record is unavailable.");
        result.Records[0].TryGetValue(field.ApiName, out var value);
        return CanonicalMutationValue(field, value);
    }
    private static void ValidatePreviewRequest(SalesforceUpdatePreviewRequest request)
    {
        if (request is null || request.Entity is null || request.Field is null || request.NewValue is null)
            throw Invalid("A complete Salesforce update request is required.");
        if (request.NewValue.Length > MaximumMutationValueLength)
            throw Invalid("The Salesforce update value is too large.");
    }
    private static PreparedUpdateDocument ReadPreparedUpdate(SalesforcePreparedUpdate preparedUpdate)
    {
        if (preparedUpdate?.Payload is not { Length: > 0 } payload || payload.Length > MaximumPreparedUpdateLength)
            throw Invalid("The prepared Salesforce update is invalid.");
        PreparedUpdateDocument? document;
        try
        {
            document = JsonConvert.DeserializeObject<PreparedUpdateDocument>(Encoding.UTF8.GetString(payload));
        }
        catch (JsonException)
        {
            throw Invalid("The prepared Salesforce update is invalid.");
        }
        if (document is null || document.Version != 1 || string.IsNullOrWhiteSpace(document.EntityLabel) || document.EntityLabel.Length > 120 ||
            string.IsNullOrWhiteSpace(document.FieldLabel) || document.FieldLabel.Length > 120 ||
            string.IsNullOrWhiteSpace(document.ObjectApiName) || document.ObjectApiName.Length > 120 ||
            string.IsNullOrWhiteSpace(document.FieldApiName) || document.FieldApiName.Length > 120 ||
            string.IsNullOrWhiteSpace(document.FieldType) || document.FieldType.Length > 40 ||
            document.DesiredValue is null || document.DesiredValue.Length > MaximumMutationValueLength ||
            document.OriginalValue is { Length: > MaximumOriginalValueLength })
            throw Invalid("The prepared Salesforce update is invalid.");
        return document;
    }
    private static (object ProviderValue, string CanonicalValue) CompileMutationValue(FieldSchema field, string value)
    {
        if (value.Length > MaximumMutationValueLength)
            throw Invalid("The Salesforce update value is too large.");
        return field.Type.ToLowerInvariant() switch
        {
            "string" or "textarea" or "picklist" or "multipicklist" or "email" or "phone" or "url" or "encryptedstring" or "combobox"
                => (value, value),
            "boolean" when bool.TryParse(value, out var parsed)
                => (parsed, parsed ? "true" : "false"),
            "int" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                => (integer, integer.ToString(CultureInfo.InvariantCulture)),
            "double" or "currency" or "percent" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                => (number, number.ToString(CultureInfo.InvariantCulture)),
            "date" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                => (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "datetime" when DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
                => (timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            "id" or "reference" => CompileReferenceMutationValue(value),
            _ => throw Invalid("That Salesforce field type is not supported for safe updates.")
        };
    }
    private static (object ProviderValue, string CanonicalValue) CompileReferenceMutationValue(string value)
    {
        ValidateRecordId(value, null);
        return (value, value);
    }
    private static string? CanonicalMutationValue(FieldSchema field, object? value)
    {
        value = value is JValue json ? json.Value : value;
        if (value is null)
            return null;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return CompileMutationValue(field, text).CanonicalValue;
    }
    private static SalesforceMutationPreviewResult PreviewFailure(SalesforceReadException exception)
    {
        var status = MutationFailureStatus(exception);
        return new SalesforceMutationPreviewResult(status, SafeReason: MutationSafeReason(status));
    }
    private static SalesforceMutationApplyResult ApplyFailure(SalesforceReadException exception)
    {
        var status = MutationFailureStatus(exception);
        return new SalesforceMutationApplyResult(status, MutationSafeReason(status));
    }
    private static SalesforceMutationStatus MutationFailureStatus(SalesforceReadException exception) => exception.Failure switch
    {
        SalesforceReadFailure.AccessDenied => SalesforceMutationStatus.AccessDenied,
        SalesforceReadFailure.InvalidRequest => SalesforceMutationStatus.InvalidRequest,
        _ => SalesforceMutationStatus.Unavailable
    };
    private static string MutationSafeReason(SalesforceMutationStatus status) => status switch
    {
        SalesforceMutationStatus.AccessDenied => "Salesforce access does not permit that update.",
        SalesforceMutationStatus.InvalidRequest => "The Salesforce update request is invalid.",
        _ => "Salesforce updates are unavailable right now."
    };
    private async Task<ObjectSchema> ResolveObjectAsync(SalesforceSemanticEntity entity, bool requireQueryable, bool requireSearchable, CancellationToken ct, bool requireUpdateable = false)
    {
        if (entity is null || string.IsNullOrWhiteSpace(entity.Label) || entity.Label.Length > 120)
            throw Invalid("A semantic Salesforce entity label is required.");
        ct.ThrowIfCancellationRequested();
        var global = await client.GetObjectsAsync<JObject>().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var matches = global.SObjects.Where(item =>
                SameLabel(Text(item, "label"), entity.Label) || SameLabel(Text(item, "labelPlural"), entity.Label))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            throw Invalid($"Salesforce entity '{entity.Label}' is unavailable or ambiguous.");
        var resolved = matches[0];
        if (requireQueryable && !Flag(resolved, "queryable"))
            throw new SalesforceReadException(SalesforceReadFailure.AccessDenied, "That Salesforce entity is not queryable for this connection.");
        if (requireSearchable && !Flag(resolved, "searchable"))
            throw new SalesforceReadException(SalesforceReadFailure.AccessDenied, "That Salesforce entity is not searchable for this connection.");
        if (requireUpdateable && !Flag(resolved, "updateable"))
            throw new SalesforceReadException(SalesforceReadFailure.AccessDenied, "That Salesforce entity is not updateable for this connection.");
        var apiName = Text(resolved, "name");
        if (string.IsNullOrWhiteSpace(apiName))
            throw Invalid("Salesforce returned incomplete entity metadata.");
        var describe = await client.DescribeAsync<JObject>(apiName).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var fields = (describe["fields"] as JArray ?? []).OfType<JObject>().Select(item => new FieldSchema(
                Text(item, "name"),
                Text(item, "label"),
                Text(item, "type"),
                Flag(item, "filterable"),
                Flag(item, "sortable"),
                Flag(item, "groupable"),
                Flag(item, "nameField"),
                (item["referenceTo"] as JArray)?.Values<string>().Where(value => value is not null).Cast<string>().ToArray() ?? [],
                Flag(item, "updateable")))
            .Where(field => !string.IsNullOrWhiteSpace(field.ApiName) && !string.IsNullOrWhiteSpace(field.Label))
            .ToArray();
        var id = fields.SingleOrDefault(field => string.Equals(field.ApiName, "Id", StringComparison.Ordinal));
        if (id is null)
            throw Invalid("Salesforce entity metadata did not expose a record identifier.");
        return new ObjectSchema(apiName, Text(resolved, "label"), Text(resolved, "labelPlural"), Text(resolved, "keyPrefix"), fields, id);
    }
    private static FieldSchema ResolveField(ObjectSchema schema, SalesforceSemanticField semantic, string purpose)
    {
        if (semantic is null || string.IsNullOrWhiteSpace(semantic.Label) || semantic.Label.Length > 120)
            throw Invalid($"A semantic Salesforce field label is required for {purpose}.");
        var matches = schema.Fields.Where(field => SameLabel(field.Label, semantic.Label)).Take(2).ToArray();
        if (matches.Length != 1)
            throw Invalid($"Salesforce field '{semantic.Label}' is unavailable or ambiguous on '{schema.Label}'.");
        return matches[0];
    }
    private static void ValidateRecordId(string value, string? keyPrefix)
    {
        if ((value.Length != 15 && value.Length != 18) || value.Any(character => !char.IsAsciiLetterOrDigit(character)) ||
            (!string.IsNullOrWhiteSpace(keyPrefix) && !value.StartsWith(keyPrefix, StringComparison.Ordinal)))
            throw Invalid("The Salesforce record reference is invalid for the resolved entity.");
    }
    private static bool SameLabel(string left, string right) =>
        string.Equals(NormalizeLabel(left), NormalizeLabel(right), StringComparison.Ordinal);
    private static string NormalizeLabel(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string Text(JObject value, string name) => value.Value<string>(name) ?? string.Empty;
    private static bool Flag(JObject value, string name) => value.Value<bool?>(name) ?? false;
    private static object? Normalize(object? value) => value switch
    {
        JValue jValue => jValue.Value,
        JObject jObject => jObject.Properties().Where(property => !string.Equals(property.Name, "attributes", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(property => property.Name, property => Normalize(property.Value)),
        JArray jArray => jArray.Select(Normalize).ToArray(),
        IDictionary<string, object?> dictionary => dictionary.Where(kv => !string.Equals(kv.Key, "attributes", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => Normalize(kv.Value)),
        _ => value
    };
    private static SalesforceReadException Invalid(string message) =>
        new(SalesforceReadFailure.InvalidRequest, message);
    private static bool IsSalesforceClientException(Exception ex) =>
        ex is not OperationCanceledException && ex.GetType().Namespace?.StartsWith("Salesforce.", StringComparison.Ordinal) == true;
    private sealed record ObjectSchema(string ApiName, string Label, string PluralLabel, string KeyPrefix, IReadOnlyList<FieldSchema> Fields, FieldSchema IdField);
    private sealed record FieldSchema(
        string ApiName,
        string Label,
        string Type,
        bool Filterable,
        bool Sortable,
        bool Groupable,
        bool NameField,
        IReadOnlyList<string> ReferenceTo,
        bool Updateable);
    private sealed record PreparedUpdateDocument(
        int Version,
        string EntityLabel,
        string ObjectApiName,
        string RecordId,
        string FieldLabel,
        string FieldApiName,
        string FieldType,
        string? OriginalValue,
        string DesiredValue);
}
