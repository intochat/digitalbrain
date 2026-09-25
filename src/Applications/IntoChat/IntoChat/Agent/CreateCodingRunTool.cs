using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.CSharpExpert;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntoChat.Agent;

internal sealed class CreateCodingRunTool(IServiceProvider services, IConfiguration configuration) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        if (services.GetService<CodingRunHost>() is not { } host)
        {
            return [];
        }

        async Task<object> Create(
            [Description("The feature to add to the C# solution, in one or two sentences.")] string description,
            [Description("Optional path to the solution file; defaults to the configured coding solution.")] string? solutionPath)
        {
            if (string.IsNullOrWhiteSpace(description) || description.Length > 2000)
            {
                return new { isError = true, message = "Describe the feature in 1–2000 characters." };
            }

            var path = string.IsNullOrWhiteSpace(solutionPath) ? configuration["DigitalBrain:Coding:SolutionPath"] : solutionPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return new { isError = true, message = "No coding solution is configured." };
            }

            try
            {
                var snapshot = await host.StartAsync(new FeatureRequest(path, description), CancellationToken.None);
                return new
                {
                    kind = "coding-run",
                    id = "coding-" + snapshot.RunId,
                    title = "Coding run",
                    runId = snapshot.RunId,
                    status = snapshot.Status.ToString(),
                };
            }
            catch (Exception error)
            {
                return new { isError = true, message = error.Message };
            }
        }

        return [AIFunctionFactory.Create(Create, "create_coding_run",
            "Start a C# coding run that drafts a plan for a feature request on the configured solution, then open the run window so the owner can clarify, approve or stop it. If isError=true, repair the arguments or explain the configuration problem.")];
    }
}
