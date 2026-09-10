using System.Text.Json;

namespace DigitalBrain.Google;

internal interface IGmailProvider
{
    Task<JsonElement> InvokeAsync(string tool, IReadOnlyDictionary<string, object?> arguments, string accessToken, CancellationToken cancellationToken);
    Task<string> ReadToolSchemaHashAsync(string tool, string accessToken, CancellationToken cancellationToken);
}
