using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Microsoft.GitHub;

// A configured repository belongs to one automation principal. Filter before returning
// tool descriptions: another principal sharing an owner must not discover its metadata.
internal sealed class GitHubRepositoryDelegation(GitHubRepositoryBinding binding, IAgentToolSource inner) : IAgentToolSource
{
    public async ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        context.RequireActive();
        cancellationToken.ThrowIfCancellationRequested();
        if (!Available(context))
        {
            return [];
        }
        var tools = await inner.GetToolsAsync(context, cancellationToken).ConfigureAwait(true);
        return tools.Select(tool => tool is AIFunction function ? new AdmittedDelegation(function, this, context) : tool).ToArray();
    }

    private bool Available(AgentToolContext context)
        => context.Owner == binding.Owner && context.Principal == binding.Principal
            && VerifiedActor.Current?.PrincipalId == binding.Principal && binding.Enabled;

    private sealed class AdmittedDelegation(AIFunction function, GitHubRepositoryDelegation source, AgentToolContext context)
        : DelegatingAIFunction(function)
    {
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            context.RequireActive();
            if (!source.Available(context))
            {
                throw new McpOperationException("The GitHub repository is not available to this principal.", McpFailureKind.AccessDenied);
            }
            return base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }
}
