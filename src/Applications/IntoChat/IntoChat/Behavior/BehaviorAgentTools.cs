using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;

namespace IntoChat;

internal sealed class BehaviorAgentTools(BehaviorToolService service) : IAgentToolFactory
{
    public static readonly string[] Names = ["code_contracts", "code_draft_read", "code_draft_save", "code_draft_check", "code_check_read", "code_check_cancel", "behavior_read", "behavior_deploy", "behavior_start", "behavior_stop", "behavior_rollback", "behavior_logs", "behavior_describe", "behavior_details"];
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        // Context is evaluated at invocation, not while the runner is discovering tools.
        return typeof(ScopedBehaviorTools).GetMethods().Where(m => m.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false).Length != 0)
            .Select(method =>
            {
                var attribute = (ModelContextProtocol.Server.McpServerToolAttribute)method.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)[0];
                return (AIFunction)new RecoverableBehaviorTool(AIFunctionFactory.Create(method, _ => service.ForScope(context().ScopeId), new AIFunctionFactoryOptions { Name = attribute.Name }));
            }).ToArray();
    }

    private sealed class RecoverableBehaviorTool(AIFunction inner) : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            try { return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or InvalidDataException or System.Text.Json.JsonException)
            {
                // Validation, revision conflicts and disabled execution are tool outcomes the
                // assistant can explain or repair. Cancellation and infrastructure faults still propagate.
                return new { isError = true, message = error.Message };
            }
        }
    }
}
