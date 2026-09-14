using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Microsoft;

public sealed class AspireConnection : IAspireResourceCommands
{
    private readonly AspireConnectionSettings? _settings;

    internal AspireConnection(AspireConnectionSettings? settings) => _settings = settings;

    public string? ApplicationName => _settings?.ApplicationName;

    public Task<JsonElement> ReadAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        // Configuration first: an unconfigured host answers what to do about it, not "not allowed".
        var settings = RequireSettings();
        if (tool is not ("list_resources" or "list_console_logs" or "list_structured_logs" or "list_traces" or "list_trace_structured_logs"))
        {
            throw new InvalidOperationException("This Aspire operation is not allowed.");
        }

        return InvokeToolAsync(settings, tool, arguments, cancellationToken);
    }

    // Promotion starts the standby and stops the retired slot (R5.2). Nothing else is executable: an
    // arbitrary command name would let a model reshape the running application.
    public Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default)
        => ExecuteResourceCommandAsync(resourceName, command, cancellationToken);

    public Task<JsonElement> ExecuteResourceCommandAsync(string resourceName, string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        var settings = RequireSettings();
        if (command is not ("start" or "stop" or "restart"))
        {
            throw new InvalidOperationException("An Aspire resource takes start, stop or restart.");
        }

        return InvokeToolAsync(settings, "execute_resource_command",
            new Dictionary<string, object?> { ["resourceName"] = resourceName, ["commandName"] = command },
            cancellationToken);
    }

    private AspireConnectionSettings RequireSettings()
        => _settings ?? throw new InvalidOperationException("Configure the Aspire AppHost project before reading or commanding its resources.");

    private async Task<JsonElement> InvokeToolAsync(AspireConnectionSettings settings, string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        CallToolResult result;
#pragma warning disable CA1031 // a broken transport, a missing CLI or an unreachable AppHost can fail in any way; every one collapses to the same "unavailable" advice
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
            result = await client.CallToolAsync(tool, arguments.ToDictionary(), cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new InvalidOperationException("The configured Aspire application is unavailable. Check its AppHost and try again.");
        }
#pragma warning restore CA1031

        // Evaluated outside the transport try: Aspire's own error text must reach the caller verbatim,
        // never collapsed into the generic transport-failure message above.
        ThrowIfFailed(result);
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

    internal static void ThrowIfFailed(CallToolResult result)
    {
        if (result.IsError != true)
        {
            return;
        }
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        throw new InvalidOperationException(text.Length == 0 ? "Aspire returned an error without a message." : "Aspire: " + text);
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
