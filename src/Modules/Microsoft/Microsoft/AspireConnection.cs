using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace DigitalBrain.Microsoft;

public sealed class AspireConnection
{
    private readonly AspireConnectionSettings? _settings;

    internal AspireConnection(AspireConnectionSettings? settings) => _settings = settings;

    public string? ApplicationName => _settings?.ApplicationName;

    public async Task<JsonElement> ReadAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var settings = _settings ?? throw new InvalidOperationException("Configure the Aspire AppHost project before reading its resources.");
        if (tool is not ("list_resources" or "list_console_logs" or "list_structured_logs" or "list_traces" or "list_trace_structured_logs"))
        {
            throw new InvalidOperationException("This Aspire operation is not allowed.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "aspire",
                Command = settings.Command,
                Arguments = ["agent", "mcp", "--non-interactive", "--log-level", "Error"],
                WorkingDirectory = Path.GetDirectoryName(settings.ProjectPath),
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            await BindApplicationAsync(client, settings, timeout.Token).ConfigureAwait(false);
            var result = await client.CallToolAsync(tool, arguments.ToDictionary(), cancellationToken: timeout.Token).ConfigureAwait(false);
            if (result.IsError == true)
            {
                throw new InvalidOperationException("Aspire did not return successful evidence.");
            }
            var envelope = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
            if (Encoding.UTF8.GetByteCount(envelope.GetRawText()) > 128 * 1024)
            {
                throw new InvalidOperationException("Aspire evidence exceeds the response budget.");
            }
            var content = JsonNode.Parse(envelope.GetRawText())!.AsObject();
            // screened at the NativeTools boundary (AI module)
            content["untrustedData"] = true;
            return JsonSerializer.SerializeToElement(content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new InvalidOperationException("The configured Aspire application is unavailable. Check its AppHost and try again.");
        }
    }

    private static async Task BindApplicationAsync(McpClient client, AspireConnectionSettings settings, CancellationToken cancellationToken)
    {
        // Discovery initializes the CLI catalog before selecting the configured AppHost.
        var discovered = await client.CallToolAsync("list_apphosts", cancellationToken: cancellationToken).ConfigureAwait(false);
        if (discovered.IsError == true)
        {
            throw new InvalidOperationException("Aspire could not discover its running applications.");
        }
        var result = await client.CallToolAsync("select_apphost",
            new Dictionary<string, object?> { ["appHostPath"] = settings.ProjectPath }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.IsError == true)
        {
            throw new InvalidOperationException("The configured Aspire application is unavailable. Start its AppHost and try again.");
        }
    }
}
