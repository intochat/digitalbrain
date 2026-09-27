using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace IntoChat;

internal sealed class CSharpAgentTools(CSharpToolService service) : IAgentToolFactory
{
    public static readonly string[] Names = ["csharp_contracts", "csharp_write", "csharp_run"];

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
        // Context is evaluated at invocation, not while the runner is discovering tools.
        => typeof(ScopedCSharpTools).GetMethods()
            .Select(method => (Method: method, Tool: method.GetCustomAttributes(typeof(McpServerToolAttribute), false).OfType<McpServerToolAttribute>().FirstOrDefault()))
            .Where(candidate => candidate.Tool is not null)
            .Select(candidate => (AIFunction)new RecoverableTool(AIFunctionFactory.Create(candidate.Method,
                _ => service.ForScope(context().ScopeId), new AIFunctionFactoryOptions { Name = candidate.Tool!.Name })))
            .ToArray();

    private sealed class RecoverableTool(AIFunction inner) : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            try { return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or TimeoutException or System.Text.Json.JsonException)
            {
                // Validation, missing files, disabled execution and a busy Docker daemon are outcomes the
                // assistant can explain or repair. Cancellation and infrastructure faults still propagate.
                return new { isError = true, message = error.Message };
            }
        }
    }
}
