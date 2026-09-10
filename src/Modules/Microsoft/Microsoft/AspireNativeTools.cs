using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Microsoft;

public sealed class AspireNativeTools(AspireConnection connection)
{
    internal AIFunction CreateRead()
    {
        Task<JsonElement> Invoke(
            [Description("One of list_resources, list_console_logs, list_structured_logs, list_traces, or list_trace_structured_logs")] string tool,
            [Description("Arguments for the selected Aspire read operation")] IReadOnlyDictionary<string, object?> arguments,
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => ReadAsync(tool, arguments, cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "aspire_read",
            Description = "Read resources, logs, or traces from the configured Aspire application.",
        });
    }

    public Task<JsonElement> ReadAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        => connection.ReadAsync(tool, arguments, cancellationToken);
}
