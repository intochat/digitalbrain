using System.Collections.ObjectModel;
using System.Text.Json;

namespace DigitalBrain.Integrations.Salesforce.Contracts;

public sealed class SalesforceRecordReference
{
    public SalesforceRecordReference(string objectName, string recordId)
    {
        ObjectName = ContractGuard.Required(objectName, nameof(objectName), 255);
        RecordId = ContractGuard.SalesforceId(recordId, nameof(recordId));
    }

    public string ObjectName { get; }
    public string RecordId { get; }
}

public sealed class SalesforceRecordReadRequest
{
    public SalesforceRecordReadRequest(SalesforceRecordReference record, IReadOnlyList<string> fields)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Fields = ContractGuard.FieldNames(fields, nameof(fields), requireItems: true);
    }

    public SalesforceRecordReference Record { get; }
    public IReadOnlyList<string> Fields { get; }
}

public sealed class SalesforceRecord
{
    public SalesforceRecord(SalesforceRecordReference reference, IReadOnlyDictionary<string, JsonElement> fields)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        Fields = ContractGuard.Fields(fields, nameof(fields));
    }

    public SalesforceRecordReference Reference { get; }
    public IReadOnlyDictionary<string, JsonElement> Fields { get; }
}

public interface ISalesforceRecordReader
{
    Task<SalesforceRecord> ReadAsync(SalesforceRecordReadRequest request, CancellationToken cancellationToken = default);
}

public sealed class SalesforceUpdateProposalRequest
{
    public SalesforceUpdateProposalRequest(SalesforceRecordReference record, string field, JsonElement newValue, string logicalOperationKey)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Field = ContractGuard.Required(field, nameof(field), 255);
        NewValue = ContractGuard.Json(newValue, nameof(newValue));
        LogicalOperationKey = ContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
    }

    public SalesforceRecordReference Record { get; }
    public string Field { get; }
    public JsonElement NewValue { get; }
    public string LogicalOperationKey { get; }
}

public sealed class SalesforceUpdateProposal
{
    public SalesforceUpdateProposal(SalesforceRecordReference record, string field, JsonElement newValue, string logicalOperationKey)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Field = ContractGuard.Required(field, nameof(field), 255);
        NewValue = ContractGuard.Json(newValue, nameof(newValue));
        LogicalOperationKey = ContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
    }

    public SalesforceRecordReference Record { get; }
    public string Field { get; }
    public JsonElement NewValue { get; }
    public string LogicalOperationKey { get; }
}

public interface ISalesforceUpdateProposer
{
    Task<SalesforceUpdateProposal> ProposeAsync(SalesforceUpdateProposalRequest request, CancellationToken cancellationToken = default);
}

internal static class ContractGuard
{
    private const int MaximumFieldCount = 100;
    private const int MaximumJsonLength = 65_536;
    private const int MaximumJsonDepth = 16;

    internal static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain 1 to {maximumLength} characters.", parameterName);
        }

        return value;
    }

    internal static string SalesforceId(string value, string parameterName)
    {
        Required(value, parameterName, 18);
        if (value.Length is not (15 or 18))
        {
            throw new ArgumentException("Salesforce identifiers must contain 15 or 18 characters.", parameterName);
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                throw new ArgumentException("Salesforce identifiers must be alphanumeric.", parameterName);
            }
        }

        return value;
    }

    internal static IReadOnlyList<string> FieldNames(IReadOnlyList<string> values, string parameterName, bool requireItems)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if ((requireItems && values.Count == 0) || values.Count > MaximumFieldCount)
        {
            throw new ArgumentException($"Collection must contain 1 to {MaximumFieldCount} items.", parameterName);
        }

        var copy = new string[values.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            var field = Required(values[index], parameterName, 255);
            if (!unique.Add(field))
            {
                throw new ArgumentException("Collection cannot contain duplicate fields.", parameterName);
            }

            copy[index] = field;
        }

        return Array.AsReadOnly(copy);
    }

    internal static IReadOnlyDictionary<string, JsonElement> Fields(IReadOnlyDictionary<string, JsonElement> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > MaximumFieldCount)
        {
            throw new ArgumentException($"Collection must contain at most {MaximumFieldCount} items.", parameterName);
        }

        var copy = new Dictionary<string, JsonElement>(values.Count, StringComparer.Ordinal);
        foreach (var pair in values)
        {
            copy.Add(Required(pair.Key, parameterName, 255), Json(pair.Value, parameterName));
        }

        return new ReadOnlyDictionary<string, JsonElement>(copy);
    }

    internal static JsonElement Json(JsonElement value, string parameterName)
    {
        if (value.ValueKind == JsonValueKind.Undefined || value.GetRawText().Length > MaximumJsonLength || Depth(value) > MaximumJsonDepth)
        {
            throw new ArgumentException($"JSON must be defined, at most {MaximumJsonLength} characters, and at most {MaximumJsonDepth} levels deep.", parameterName);
        }

        return value.Clone();
    }

    private static int Depth(JsonElement value)
    {
        var maximumChildDepth = 0;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                maximumChildDepth = Math.Max(maximumChildDepth, Depth(property.Value));
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                maximumChildDepth = Math.Max(maximumChildDepth, Depth(item));
            }
        }

        return maximumChildDepth + 1;
    }
}
