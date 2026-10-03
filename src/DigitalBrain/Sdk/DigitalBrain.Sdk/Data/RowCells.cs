using System.Text.Json;

namespace DigitalBrain.Sdk.Data;

// Query cells arrive as JSON text. A row value is the cell's text, with JSON strings unwrapped.
public static class RowCells
{
    public static string FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") { return ""; }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => document.RootElement.GetRawText(),
            };
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
