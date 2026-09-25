using DigitalBrain.CSharpExpert;
using DigitalBrain.Microsoft.Roslyn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Tests;

internal static class CSharpExpertTestHost
{
    public static string SampleSolution => Path.Combine(AppContext.BaseDirectory, "Assets", "SampleInbox", "SampleInbox.sln");

    public static Task<UnitBrain> StartAsync(CancellationToken cancellationToken, ScriptedAgentScript? script = null)
    {
        var builder = UnitTest.Create()
            .WithModule<CSharpExpertModule>()
            .WithModule<RoslynModule>()
            .WithExecution(new TestExecutionOptions
            {
                StartupTimeout = TimeSpan.FromMinutes(3),
                AssertionTimeout = TimeSpan.FromSeconds(30),
                CleanupTimeout = TimeSpan.FromSeconds(30),
                PrivateConfiguration = new Dictionary<string, string?>
                {
                    ["DigitalBrain:Coding:SolutionPath"] = SampleSolution,
                    ["DigitalBrain:Coding:WorkspaceKey"] = CodingWorkspace.Id(SampleSolution),
                },
            })
            .ConfigureSilo(silo =>
            {
                silo.Services.Replace(ServiceDescriptor.Singleton<ISolutionLoader>(new SampleSolutionLoader()));
                if (script is not null)
                {
                    silo.Services.Replace(ServiceDescriptor.Singleton<ICodingAgentBackend>(new ScriptedCodingBackend(script)));
                }
            });
        return builder.StartAsync(cancellationToken);
    }
}
