using System.Globalization;
using System.Text;
using DigitalBrain.Kernel.V2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salesforce.Common.Models.Json;
using Salesforce.Force;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceApiClient(ForceClient client, string? identityUrl = null) : ISalesforceApiClient
{
    private const int MaximumResults = 200;

    public async Task<string> GetCurrentUserProfileAsync(CancellationToken ct)
    {
        if (!IsAllowedIdentityUrl(identityUrl))
            throw new InvalidOperationException("Salesforce identity information is unavailable. Reconnect Salesforce to continue.");

        ct.ThrowIfCancellationRequested();
        try
        {
            var profile = await client.UserInfo<UserInfo>(identityUrl).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return JsonConvert.SerializeObject(new
            {
                profile.UserId,
                profile.OrganizationId,
                profile.DisplayName,
                profile.Username,
                profile.Email,
                profile.UserType,
                profile.Active,
                profile.Locale,
                profile.Language
            });
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw new InvalidOperationException($"Salesforce profile read failed: {ex.Message}", ex);
        }
    }

    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct) =>
        LegacyReadAsync(
            $"SELECT Id, Name, Type, Industry, Website, BillingCity, BillingCountry, LastModifiedDate FROM Account ORDER BY LastModifiedDate DESC LIMIT {Math.Clamp(maxResults, 1, 50)}",
            ct);

    public Task<string[]> ListContactsAsync(int maxResults, CancellationToken ct) =>
        LegacyReadAsync(
            $"SELECT Id, Name, Title, Email, Phone, Account.Name, LastModifiedDate FROM Contact ORDER BY LastModifiedDate DESC LIMIT {Math.Clamp(maxResults, 1, 50)}",
            ct);

    public async Task<string> DescribeCrmAccessAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var account = await client.DescribeAsync<JObject>("Account").ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var contact = await client.DescribeAsync<JObject>("Contact").ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return JsonConvert.SerializeObject(new
            {
                Account = SummarizeDescribe(account),
                Contact = SummarizeDescribe(contact)
            });
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw new InvalidOperationException($"Salesforce metadata read failed: {ex.Message}", ex);
        }
    }

    public async Task<SalesforceReadPage> DiscoverObjectsAsync(
        V2SalesforceDiscoveryRequest request,
        CancellationToken ct)
    {
        var scope = ProviderScope();
        ct.ThrowIfCancellationRequested();
        try
        {
            var global = await client.GetObjectsAsync<JObject>().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var objects = global.SObjects
                .Where(item => Flag(item, "queryable") || Flag(item, "searchable"))
                .OrderBy(item => Text(item, "label"), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => Text(item, "name"), StringComparer.Ordinal)
                .Take(Math.Clamp(request.Limit, 1, MaximumResults))
                .Select(item => new
                {
                    Label = Text(item, "label"),
                    PluralLabel = Text(item, "labelPlural"),
                    Queryable = Flag(item, "queryable"),
                    Searchable = Flag(item, "searchable")
                })
                .ToArray();
            return new SalesforceReadPage(
                JsonConvert.SerializeObject(new { Objects = objects }),
                objects.Length,
                objects.Length,
                scope);
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw Classify(ex, "Salesforce object discovery failed.");
        }
    }

    public async Task<SalesforceReadPage> ReadRecordsAsync(
        V2SalesforceRecordReadRequest request,
        CancellationToken ct)
    {
        var scope = ProviderScope();
        try
        {
            var schema = await ResolveObjectAsync(request.Entity, requireQueryable: true, requireSearchable: false, ct).ConfigureAwait(false);
            var selected = ResolveSelectedFields(schema, request.Fields);
            var where = new List<string>(CompileFilters(schema, request.Filters));

            if (request.Kind == V2SalesforceRecordReadKind.Details)
            {
                if (request.Record is null || !SameLabel(request.Record.Entity.Label, request.Entity.Label))
                    throw Invalid("A server-resolved record for the requested Salesforce entity is required.");
                ValidateRecordId(request.Record.RecordId, schema.KeyPrefix);
                where.Add($"{schema.IdField.ApiName} = '{request.Record.RecordId}'");
            }
            else if (request.Kind == V2SalesforceRecordReadKind.Related)
            {
                if (request.RelatedTo is null)
                    throw Invalid("A server-resolved parent record is required for a related-record read.");
                var parent = await ResolveObjectAsync(request.RelatedTo.Entity, true, false, ct).ConfigureAwait(false);
                ValidateRecordId(request.RelatedTo.RecordId, parent.KeyPrefix);
                var relation = ResolveRelationship(schema, parent, request.Relationship);
                where.Add($"{relation.ApiName} = '{request.RelatedTo.RecordId}'");
            }

            var order = CompileSorts(schema, request.Sorts);
            var limit = Math.Clamp(request.Limit, 1, MaximumResults);
            var soql = $"SELECT {string.Join(", ", selected.Select(field => field.ApiName))} FROM {schema.ApiName}" +
                       (where.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", where)}") +
                       $" ORDER BY {order} LIMIT {limit}";
            ct.ThrowIfCancellationRequested();
            var result = await client.QueryAsync<Dictionary<string, object?>>(soql).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return Page(schema, selected, result, scope);
        }
        catch (SalesforceReadException)
        {
            throw;
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw Classify(ex, "Salesforce record read failed.");
        }
    }

    public async Task<SalesforceReadPage> SearchRecordsAsync(
        V2SalesforceSearchRequest request,
        CancellationToken ct)
    {
        var scope = ProviderScope();
        if (string.IsNullOrWhiteSpace(request.SearchText) || request.SearchText.Length > 256)
            throw Invalid("Salesforce search text must contain between 1 and 256 characters.");
        try
        {
            var entities = request.Entities is { Count: > 0 }
                ? request.Entities.Take(5).ToArray()
                : throw Invalid("At least one semantic Salesforce entity is required for search.");
            var schemas = new List<ObjectSchema>(entities.Length);
            foreach (var entity in entities)
                schemas.Add(await ResolveObjectAsync(entity, false, true, ct).ConfigureAwait(false));

            var limit = Math.Clamp(request.Limit, 1, MaximumResults);
            var perObject = Math.Max(1, limit / schemas.Count);
            var returning = schemas.Select(schema =>
            {
                var fields = ResolveSelectedFields(schema, null);
                return $"{schema.ApiName}({string.Join(", ", fields.Select(field => field.ApiName))} LIMIT {perObject})";
            });
            var sosl = $"FIND {{{EscapeSosl(request.SearchText)}}} IN ALL FIELDS RETURNING {string.Join(", ", returning)}";
            ct.ThrowIfCancellationRequested();
            var raw = await client.SearchAsync<Dictionary<string, object?>>(sosl).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var records = raw.Take(limit).Select(record => ProjectSearchRecord(record, schemas)).ToArray();
            return new SalesforceReadPage(
                JsonConvert.SerializeObject(new { Records = records }),
                records.Length,
                records.Length,
                scope);
        }
        catch (SalesforceReadException)
        {
            throw;
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw Classify(ex, "Salesforce search failed.");
        }
    }

    public async Task<SalesforceReadPage> AggregateRecordsAsync(
        V2SalesforceAggregateRequest request,
        CancellationToken ct)
    {
        var scope = ProviderScope();
        try
        {
            var schema = await ResolveObjectAsync(request.Entity, true, false, ct).ConfigureAwait(false);
            var field = request.Field is null ? null : ResolveField(schema, request.Field, "aggregate");
            if (request.Function != V2SemanticAggregateFunction.Count && field is null)
                throw Invalid("The requested Salesforce aggregate requires a semantic field.");
            var group = request.GroupBy is null ? null : ResolveField(schema, request.GroupBy, "group");
            if (group is not null && !group.Groupable)
                throw Invalid($"Salesforce field '{group.Label}' cannot be grouped.");

            var expression = request.Function switch
            {
                V2SemanticAggregateFunction.Count => "COUNT()",
                V2SemanticAggregateFunction.CountDistinct => $"COUNT_DISTINCT({field!.ApiName})",
                V2SemanticAggregateFunction.Sum => $"SUM({field!.ApiName})",
                V2SemanticAggregateFunction.Average => $"AVG({field!.ApiName})",
                V2SemanticAggregateFunction.Minimum => $"MIN({field!.ApiName})",
                V2SemanticAggregateFunction.Maximum => $"MAX({field!.ApiName})",
                _ => throw Invalid("The requested Salesforce aggregate is unsupported.")
            };
            var filters = CompileFilters(schema, request.Filters).ToArray();
            var soql = $"SELECT {(group is null ? string.Empty : group.ApiName + ", ")}{expression} value FROM {schema.ApiName}" +
                       (filters.Length == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}") +
                       (group is null ? string.Empty : $" GROUP BY {group.ApiName}") +
                       $" LIMIT {Math.Clamp(request.Limit, 1, MaximumResults)}";
            ct.ThrowIfCancellationRequested();
            var result = await client.QueryAsync<Dictionary<string, object?>>(soql).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var rows = result.Records.Select(record => Normalize(record)).ToArray();
            return new SalesforceReadPage(
                JsonConvert.SerializeObject(new
                {
                    Entity = schema.Label,
                    Function = request.Function.ToString(),
                    Field = field?.Label,
                    GroupBy = group?.Label,
                    Rows = rows
                }),
                rows.Length,
                result.TotalSize,
                scope);
        }
        catch (SalesforceReadException)
        {
            throw;
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw Classify(ex, "Salesforce aggregate read failed.");
        }
    }

    public async Task<SalesforceReadPage> ContinueRecordsAsync(
        SalesforceContinuation continuation,
        CancellationToken ct)
    {
        var scope = ProviderScope();
        if (continuation is null || continuation.Scope != scope || !IsAllowedContinuation(continuation.NextRecordsUrl))
            throw new SalesforceReadException(SalesforceReadFailure.ContinuationExpired, "That Salesforce continuation is no longer available.");
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await client.QueryContinuationAsync<Dictionary<string, object?>>(continuation.NextRecordsUrl).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var records = result.Records.Select(record => ProjectContinuationRecord(continuation, record)).ToArray();
            return new SalesforceReadPage(
                JsonConvert.SerializeObject(new { Entity = continuation.EntityLabel, Records = records }),
                records.Length,
                result.TotalSize,
                scope,
                Next(result, continuation));
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw Classify(ex, "Salesforce continuation failed.");
        }
    }

    private async Task<ObjectSchema> ResolveObjectAsync(
        V2SalesforceSemanticEntity entity,
        bool requireQueryable,
        bool requireSearchable,
        CancellationToken ct)
    {
        if (entity is null || string.IsNullOrWhiteSpace(entity.Label) || entity.Label.Length > 120)
            throw Invalid("A semantic Salesforce entity label is required.");
        ct.ThrowIfCancellationRequested();
        var global = await client.GetObjectsAsync<JObject>().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var matches = global.SObjects.Where(item =>
                SameLabel(Text(item, "label"), entity.Label) ||
                SameLabel(Text(item, "labelPlural"), entity.Label))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            throw Invalid($"Salesforce entity '{entity.Label}' is unavailable or ambiguous.");
        var resolved = matches[0];
        if (requireQueryable && !Flag(resolved, "queryable"))
            throw new SalesforceReadException(SalesforceReadFailure.AccessDenied, "That Salesforce entity is not queryable for this connection.");
        if (requireSearchable && !Flag(resolved, "searchable"))
            throw new SalesforceReadException(SalesforceReadFailure.AccessDenied, "That Salesforce entity is not searchable for this connection.");
        var apiName = Text(resolved, "name");
        if (string.IsNullOrWhiteSpace(apiName))
            throw Invalid("Salesforce returned incomplete entity metadata.");
        var describe = await client.DescribeAsync<JObject>(apiName).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var fields = (describe["fields"] as JArray ?? [])
            .OfType<JObject>()
            .Select(item => new FieldSchema(
                Text(item, "name"),
                Text(item, "label"),
                Text(item, "type"),
                Flag(item, "filterable"),
                Flag(item, "sortable"),
                Flag(item, "groupable"),
                Flag(item, "nameField"),
                (item["referenceTo"] as JArray)?.Values<string>().Where(value => value is not null).Cast<string>().ToArray() ?? []))
            .Where(field => !string.IsNullOrWhiteSpace(field.ApiName) && !string.IsNullOrWhiteSpace(field.Label))
            .ToArray();
        var id = fields.SingleOrDefault(field => string.Equals(field.ApiName, "Id", StringComparison.Ordinal));
        if (id is null)
            throw Invalid("Salesforce entity metadata did not expose a record identifier.");
        return new ObjectSchema(
            apiName,
            Text(resolved, "label"),
            Text(resolved, "labelPlural"),
            Text(resolved, "keyPrefix"),
            fields,
            id);
    }

    private static IReadOnlyList<FieldSchema> ResolveSelectedFields(
        ObjectSchema schema,
        IReadOnlyList<V2SalesforceSemanticField>? requested)
    {
        var result = new List<FieldSchema> { schema.IdField };
        if (requested is { Count: > 0 })
        {
            foreach (var semantic in requested.Take(20))
            {
                var field = ResolveField(schema, semantic, "read");
                if (result.All(existing => !string.Equals(existing.ApiName, field.ApiName, StringComparison.Ordinal)))
                    result.Add(field);
            }
        }
        else
        {
            var name = schema.Fields.FirstOrDefault(field => field.NameField);
            if (name is not null && name != schema.IdField)
                result.Add(name);
        }
        return result;
    }

    private static FieldSchema ResolveField(
        ObjectSchema schema,
        V2SalesforceSemanticField semantic,
        string purpose)
    {
        if (semantic is null || string.IsNullOrWhiteSpace(semantic.Label) || semantic.Label.Length > 120)
            throw Invalid($"A semantic Salesforce field label is required for {purpose}.");
        var matches = schema.Fields.Where(field => SameLabel(field.Label, semantic.Label)).Take(2).ToArray();
        if (matches.Length != 1)
            throw Invalid($"Salesforce field '{semantic.Label}' is unavailable or ambiguous on '{schema.Label}'.");
        return matches[0];
    }

    private static IEnumerable<string> CompileFilters(
        ObjectSchema schema,
        IReadOnlyList<V2SalesforceFilter>? filters)
    {
        foreach (var filter in filters?.Take(12) ?? [])
        {
            var field = ResolveField(schema, filter.Field, "filtering");
            if (!field.Filterable)
                throw Invalid($"Salesforce field '{field.Label}' cannot be filtered.");
            if (filter.Operator is V2SemanticFilterOperator.IsNull or V2SemanticFilterOperator.IsNotNull)
            {
                yield return $"{field.ApiName} {(filter.Operator == V2SemanticFilterOperator.IsNull ? "=" : "!=")} null";
                continue;
            }
            if (string.IsNullOrWhiteSpace(filter.Value))
                throw Invalid($"A value is required for Salesforce field '{field.Label}'.");
            var literal = Literal(field, filter.Value);
            yield return filter.Operator switch
            {
                V2SemanticFilterOperator.Equals => $"{field.ApiName} = {literal}",
                V2SemanticFilterOperator.NotEquals => $"{field.ApiName} != {literal}",
                V2SemanticFilterOperator.GreaterThan => $"{field.ApiName} > {literal}",
                V2SemanticFilterOperator.GreaterThanOrEqual => $"{field.ApiName} >= {literal}",
                V2SemanticFilterOperator.LessThan => $"{field.ApiName} < {literal}",
                V2SemanticFilterOperator.LessThanOrEqual => $"{field.ApiName} <= {literal}",
                V2SemanticFilterOperator.Contains => $"{field.ApiName} LIKE '%{EscapeLike(filter.Value)}%'",
                V2SemanticFilterOperator.StartsWith => $"{field.ApiName} LIKE '{EscapeLike(filter.Value)}%'",
                _ => throw Invalid($"The requested filter operation is unsupported for Salesforce field '{field.Label}'.")
            };
        }
    }

    private static string CompileSorts(ObjectSchema schema, IReadOnlyList<V2SalesforceSort>? sorts)
    {
        var resolved = new List<(FieldSchema Field, V2SemanticSortDirection Direction)>();
        foreach (var sort in sorts?.Take(5) ?? [])
        {
            var field = ResolveField(schema, sort.Field, "sorting");
            if (!field.Sortable)
                throw Invalid($"Salesforce field '{field.Label}' cannot be sorted.");
            if (resolved.All(item => item.Field.ApiName != field.ApiName))
                resolved.Add((field, sort.Direction));
        }
        if (resolved.All(item => item.Field.ApiName != schema.IdField.ApiName))
            resolved.Add((schema.IdField, V2SemanticSortDirection.Ascending));
        return string.Join(", ", resolved.Select(item =>
            $"{item.Field.ApiName} {(item.Direction == V2SemanticSortDirection.Descending ? "DESC" : "ASC")}"));
    }

    private static FieldSchema ResolveRelationship(
        ObjectSchema child,
        ObjectSchema parent,
        V2SalesforceSemanticField? semantic)
    {
        var candidates = child.Fields.Where(field =>
            field.ReferenceTo.Contains(parent.ApiName, StringComparer.Ordinal)).ToArray();
        if (semantic is not null)
            candidates = candidates.Where(field => SameLabel(field.Label, semantic.Label)).ToArray();
        if (candidates.Length != 1)
            throw Invalid($"The relationship from '{child.Label}' to '{parent.Label}' is unavailable or ambiguous.");
        return candidates[0];
    }

    private static string Literal(FieldSchema field, string value) => field.Type.ToLowerInvariant() switch
    {
        "boolean" when bool.TryParse(value, out var parsed) => parsed ? "true" : "false",
        "int" or "double" or "currency" or "percent" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            => number.ToString(CultureInfo.InvariantCulture),
        "date" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "datetime" when DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            => timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        "id" or "reference" => ValidateAndQuoteId(value),
        _ => $"'{EscapeSoql(value)}'"
    };

    private static string ValidateAndQuoteId(string value)
    {
        ValidateRecordId(value, null);
        return $"'{value}'";
    }

    private static SalesforceReadPage Page(
        ObjectSchema schema,
        IReadOnlyList<FieldSchema> selected,
        QueryResult<Dictionary<string, object?>> result,
        SalesforceProviderScope scope)
    {
        var records = result.Records.Select(record => ProjectRecord(schema, selected, record)).ToArray();
        return new SalesforceReadPage(
            JsonConvert.SerializeObject(new { Entity = schema.Label, Records = records }),
            records.Length,
            result.TotalSize,
            scope,
            Next(result, scope, schema, selected));
    }

    private static object ProjectRecord(
        ObjectSchema schema,
        IReadOnlyList<FieldSchema> selected,
        IDictionary<string, object?> record)
    {
        var values = selected.Where(field => field != schema.IdField)
            .ToDictionary(
                field => field.Label,
                field => record.TryGetValue(field.ApiName, out var value) ? Normalize(value) : null,
                StringComparer.Ordinal);
        record.TryGetValue(schema.IdField.ApiName, out var id);
        return new { Entity = schema.Label, RecordId = Normalize(id), Fields = values };
    }

    private static object ProjectSearchRecord(
        IDictionary<string, object?> record,
        IReadOnlyList<ObjectSchema> schemas)
    {
        var type = (record.TryGetValue("attributes", out var attributes) ? attributes : null) switch
        {
            JObject json => json.Value<string>("type"),
            IDictionary<string, object?> map when map.TryGetValue("type", out var value) => value?.ToString(),
            _ => null
        };
        var schema = schemas.FirstOrDefault(item => item.ApiName == type);
        if (schema is null)
            return Normalize(record)!;
        return ProjectRecord(schema, ResolveSelectedFields(schema, null), record);
    }

    private static object ProjectContinuationRecord(
        SalesforceContinuation continuation,
        IDictionary<string, object?> record)
    {
        var values = continuation.FieldLabels.ToDictionary(
            field => field.Value,
            field => record.TryGetValue(field.Key, out var value) ? Normalize(value) : null,
            StringComparer.Ordinal);
        record.TryGetValue(continuation.RecordIdField, out var id);
        return new { Entity = continuation.EntityLabel, RecordId = Normalize(id), Fields = values };
    }

    private async Task<string[]> LegacyReadAsync(string soql, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = await client.QueryAsync<Dictionary<string, object?>>(soql).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return result.Records.Select(record => JsonConvert.SerializeObject(Normalize(record))).ToArray();
        }
        catch (Exception ex) when (IsSalesforceClientException(ex))
        {
            throw new InvalidOperationException($"Salesforce query failed: {ex.Message}", ex);
        }
    }

    private SalesforceProviderScope ProviderScope()
    {
        if (!IsAllowedIdentityUrl(identityUrl) || !Uri.TryCreate(identityUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Salesforce identity information is unavailable. Reconnect Salesforce to continue.");
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 || segments[0] != "id")
            throw new InvalidOperationException("Salesforce identity information is unavailable. Reconnect Salesforce to continue.");
        return new SalesforceProviderScope(segments[1], segments[2]);
    }

    private static SalesforceContinuation? Next(
        QueryResult<Dictionary<string, object?>> result,
        SalesforceProviderScope scope,
        ObjectSchema schema,
        IReadOnlyList<FieldSchema> selected) =>
        result.Done || string.IsNullOrWhiteSpace(result.NextRecordsUrl)
            ? null
            : new SalesforceContinuation(
                result.NextRecordsUrl,
                scope,
                schema.Label,
                schema.IdField.ApiName,
                selected.Where(field => field != schema.IdField)
                    .ToDictionary(field => field.ApiName, field => field.Label, StringComparer.Ordinal));

    private static SalesforceContinuation? Next(
        QueryResult<Dictionary<string, object?>> result,
        SalesforceContinuation previous) =>
        result.Done || string.IsNullOrWhiteSpace(result.NextRecordsUrl)
            ? null
            : new SalesforceContinuation(
                result.NextRecordsUrl,
                previous.Scope,
                previous.EntityLabel,
                previous.RecordIdField,
                previous.FieldLabels);

    private static void ValidateRecordId(string value, string? keyPrefix)
    {
        if ((value.Length != 15 && value.Length != 18) || value.Any(character => !char.IsAsciiLetterOrDigit(character)) ||
            (!string.IsNullOrWhiteSpace(keyPrefix) && !value.StartsWith(keyPrefix, StringComparison.Ordinal)))
            throw Invalid("The Salesforce record reference is invalid for the resolved entity.");
    }

    private static bool IsAllowedContinuation(string value) =>
        Uri.TryCreate(value, UriKind.Relative, out _) &&
        value.StartsWith("/services/data/", StringComparison.Ordinal) &&
        value.Contains("/query/", StringComparison.Ordinal);

    private static string EscapeSoql(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    private static string EscapeLike(string value) => EscapeSoql(value)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static string EscapeSosl(string value)
    {
        const string reserved = "?&|!{}[]()^~*:\\\"'+-";
        var result = new StringBuilder(value.Length * 2);
        foreach (var character in value)
        {
            if (reserved.Contains(character, StringComparison.Ordinal)) result.Append('\\');
            result.Append(character);
        }
        return result.ToString();
    }

    private static bool SameLabel(string left, string right) =>
        string.Equals(NormalizeLabel(left), NormalizeLabel(right), StringComparison.Ordinal);

    private static string NormalizeLabel(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Text(JObject value, string name) => value.Value<string>(name) ?? string.Empty;
    private static bool Flag(JObject value, string name) => value.Value<bool?>(name) ?? false;

    private static object SummarizeDescribe(JObject describe) => new
    {
        Name = describe.Value<string>("name"),
        Label = describe.Value<string>("label"),
        Queryable = describe.Value<bool?>("queryable") ?? false,
        Searchable = describe.Value<bool?>("searchable") ?? false,
        AccessibleFields = (describe["fields"] as JArray ?? [])
            .OfType<JObject>()
            .Select(field => new
            {
                Name = field.Value<string>("name"),
                Label = field.Value<string>("label"),
                Type = field.Value<string>("type")
            })
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToArray()
    };

    private static object? Normalize(object? value) => value switch
    {
        JValue jValue => jValue.Value,
        JObject jObject => jObject.Properties()
            .Where(property => !string.Equals(property.Name, "attributes", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(property => property.Name, property => Normalize(property.Value)),
        JArray jArray => jArray.Select(Normalize).ToArray(),
        IDictionary<string, object?> dictionary => dictionary
            .Where(kv => !string.Equals(kv.Key, "attributes", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => Normalize(kv.Value)),
        _ => value
    };

    private static SalesforceReadException Invalid(string message) =>
        new(SalesforceReadFailure.InvalidRequest, message);

    private static SalesforceReadException Classify(Exception exception, string fallback)
    {
        var message = exception.GetBaseException().Message;
        if (message.Contains("REQUEST_LIMIT_EXCEEDED", StringComparison.OrdinalIgnoreCase))
            return new SalesforceReadException(SalesforceReadFailure.LimitReached, "Salesforce API limits have been reached. Try again later.", exception);
        if (message.Contains("INSUFFICIENT_ACCESS", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("INVALID_FIELD", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            return new SalesforceReadException(SalesforceReadFailure.AccessDenied, "Salesforce access does not permit that read.", exception);
        return new SalesforceReadException(SalesforceReadFailure.Unsupported, fallback, exception);
    }

    private static bool IsAllowedIdentityUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "login.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "test.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".my.salesforce.com", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.StartsWith("/id/", StringComparison.Ordinal);

    private static bool IsSalesforceClientException(Exception ex) =>
        ex is not OperationCanceledException &&
        ex.GetType().Namespace?.StartsWith("Salesforce.", StringComparison.Ordinal) == true;

    private sealed record ObjectSchema(
        string ApiName,
        string Label,
        string PluralLabel,
        string KeyPrefix,
        IReadOnlyList<FieldSchema> Fields,
        FieldSchema IdField);

    private sealed record FieldSchema(
        string ApiName,
        string Label,
        string Type,
        bool Filterable,
        bool Sortable,
        bool Groupable,
        bool NameField,
        IReadOnlyList<string> ReferenceTo);
}
