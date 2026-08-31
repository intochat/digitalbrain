using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Integrations.Salesforce;

internal sealed partial class SalesforceToolSource(ISalesforceTransport transport) : IAgentToolSource
{
    public IReadOnlyList<AIFunction> ToolsFor(OwnerId owner)
    {
        // The credential belongs to the configured kernel, never to model-supplied arguments.
        _ = owner;
        return
        [
            AIFunctionFactory.Create(transport.GetUserInfoJsonAsync, new AIFunctionFactoryOptions
            {
                Name = "salesforce_get_current_user",
                Description = "Read-only authentication check: get the Salesforce user authenticated with the hosted MCP server.",
            }),
            AIFunctionFactory.Create(QueryAsync, new AIFunctionFactoryOptions
            {
                Name = "salesforce_soql_query",
                Description = "Read Salesforce using a single SELECT SOQL query. An outer WHERE filter and positive LIMIT are required. No writes or locking queries.",
            }),
            AIFunctionFactory.Create(CreateOrUpdateAsync, new AIFunctionFactoryOptions
            {
                Name = "salesforce_create_or_update",
                Description = "Preview or execute a Salesforce create/update. First use confirmed=false and show the preview to the user. Only use confirmed=true after explicit user confirmation of those exact changes. Never delete.",
            }),
        ];
    }

    private Task<string> QueryAsync(
        [Description("A single SELECT query with an outer WHERE and a positive LIMIT, for example SELECT Id, Name FROM Account WHERE Name = 'Acme' LIMIT 10.")] string query,
        CancellationToken cancellationToken)
    {
        SalesforceQueryGuard.Validate(query);
        return transport.QueryJsonAsync(query, cancellationToken);
    }

    private async Task<string> CreateOrUpdateAsync(
        [Description("Salesforce object API name, for example Account or Contact.")] string objectType,
        [Description("JSON object containing only the Salesforce field-value pairs to write.")] string bodyJson,
        [Description("Required. False previews without writing. True only after the user explicitly confirms the exact preview.")] bool confirmed,
        [Description("Existing Salesforce record ID for an update; null for a create.")] string? id = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(objectType) || !ObjectName().IsMatch(objectType))
        {
            return "Provide a Salesforce object API name, such as Account.";
        }
        if (id is not null && !RecordId().IsMatch(id))
        {
            return "Provide a 15- or 18-character Salesforce record ID for an update, or null for a create.";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bodyJson);
        }
        catch (JsonException)
        {
            return "bodyJson must be a valid JSON object of Salesforce field-value pairs.";
        }

        using (document)
        {
            var body = document.RootElement;
            if (body.ValueKind != JsonValueKind.Object || !body.EnumerateObject().Any())
            {
                return "bodyJson must contain a non-empty JSON object of Salesforce field-value pairs.";
            }

            if (!confirmed)
            {
                return JsonSerializer.Serialize(new
                {
                    confirmationRequired = true,
                    message = "No Salesforce mutation was made. Ask the user to confirm these exact changes before calling again with confirmed=true.",
                    operation = id is null ? "createRecord" : "updateRecord",
                    objectType,
                    id,
                    body,
                });
            }

            var payload = JsonSerializer.Serialize(new { id, body, confirmed });
            return await transport.UpsertJsonAsync(objectType, payload, cancellationToken).ConfigureAwait(false);
        }
    }

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectName();

    [GeneratedRegex(@"\A[A-Za-z0-9]{15}([A-Za-z0-9]{3})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex RecordId();
}
