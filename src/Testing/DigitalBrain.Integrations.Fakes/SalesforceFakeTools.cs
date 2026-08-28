using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DigitalBrain.Integrations.Fakes;

[McpServerToolType]
internal sealed class SalesforceFakeTools(SalesforceFakeStore store)
{
    [McpServerTool(Name = "getObjectSchema", UseStructuredContent = true, OutputSchemaType = typeof(SalesforceMcpOutput), ReadOnly = true, Idempotent = true)]
    public JsonElement GetObjectSchema(string? objectName = null)
        => JsonSerializer.SerializeToElement(objectName is null
            ? new { objects = new[] { new { name = "Account", description = "A company account." } } }
            : new
            {
                objects = new[]
                {
                    new { name = objectName, description = "A company account." },
                },
            });

    [McpServerTool(Name = "soqlQuery", UseStructuredContent = true, OutputSchemaType = typeof(SalesforceMcpOutput), ReadOnly = true, Idempotent = true)]
    [Description("Executes a deterministic fake SOQL query.")]
    public JsonElement SoqlQuery(string query) => store.Query(query);

    [McpServerTool(Name = "soslSearch", UseStructuredContent = true, OutputSchemaType = typeof(SalesforceMcpOutput), ReadOnly = true, Idempotent = true)]
    public JsonElement SoslSearch(string search)
        => JsonSerializer.SerializeToElement(new
        {
            searchRecords = new[] { new { Id = "001INTOCHAT", Name = search.Contains("IntoChat", StringComparison.OrdinalIgnoreCase) ? "IntoChat" : "Acme" } },
        });

    [McpServerTool(Name = "createRecord", UseStructuredContent = true, OutputSchemaType = typeof(SalesforceMcpOutput))]
    public JsonElement CreateRecord(JsonElement body, string? sobjectName = null)
        => store.Create(body, sobjectName ?? "Account");

    [McpServerTool(Name = "updateRecord", UseStructuredContent = true, OutputSchemaType = typeof(SalesforceMcpOutput), Idempotent = true)]
    public JsonElement UpdateRecord(string id, JsonElement body, string? sobjectName = null)
        => store.Update(id, body, sobjectName ?? "Account");

    [McpServerTool(Name = "updateRelatedRecord", UseStructuredContent = true, OutputSchemaType = typeof(SalesforceMcpOutput), Idempotent = true)]
    public JsonElement UpdateRelatedRecord(
        string id,
        JsonElement body,
        string? sobjectName = null,
        string? relationshipPath = null)
    {
        _ = relationshipPath;
        return store.Update(id, body, sobjectName ?? "Account");
    }
}
