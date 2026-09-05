using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

public abstract partial class Agent
{
    private AIFunction ObserveMcpTool(AIFunction function)
        => function.GetService<McpDiscoveredTool>() is { } tool
            ? new ObservedMcpTool(this, function, tool.ConnectionName) : function;

    private sealed class ObservedMcpTool(Agent agent, AIFunction function, string server) : DelegatingAIFunction(function)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var operation = Guid.NewGuid();
            var started = Stopwatch.GetTimestamp();
            await agent.RecordOutgoingAsync(new AgentActivity(operation, "tool", "started", Name, Server: server))
                .ConfigureAwait(true);
            var state = "failed";
            string? preview = null;
            var isError = false;
            var truncated = false;
            try
            {
                var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(true);
                if (result is JsonElement nativeResult)
                {
                    result = McpEvidencePreview.Redact(nativeResult);
                }
                var json = result is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(result);
                var screen = agent.ServiceProvider.GetRequiredService<IUntrustedContentScreen>();
                await screen.ScreenAsync(json, cancellationToken).ConfigureAwait(true);
                preview = McpEvidencePreview.Create(json);
                isError = McpDiscoveredTool.IsError(result);
                truncated = McpDiscoveredTool.IsTruncated(result);
                state = isError ? "failed" : "completed";
                return result;
            }
            catch (OperationCanceledException)
            {
                state = "cancelled";
                throw;
            }
            catch (Exception)
            {
                // Invocation pipelines can show exception messages to the model.
                // Never expose a process command line, credential or raw transport error.
                throw new InvalidOperationException("The MCP tool failed or its content could not pass screening. No successful observation is available.");
            }
            finally
            {
                await agent.RecordOutgoingAsync(new AgentActivity(operation, "tool", state, Name, Server: server,
                    DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds, Preview: preview,
                    IsError: isError, Truncated: truncated)).ConfigureAwait(true);
            }
        }
    }
}
