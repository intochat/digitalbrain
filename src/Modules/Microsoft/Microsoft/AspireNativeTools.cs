using System.Text.Json;

namespace DigitalBrain.Microsoft;

public sealed class AspireNativeTools(AspireConnection connection)
{
    public Task<JsonElement> ReadAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        => connection.ReadAsync(tool, arguments, cancellationToken);
}
