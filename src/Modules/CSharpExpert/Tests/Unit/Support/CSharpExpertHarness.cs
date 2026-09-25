using DigitalBrain.CSharpExpert;
using DigitalBrain.Microsoft.DotNet;
using DigitalBrain.Microsoft.Roslyn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Tests;

internal sealed class CSharpExpertHarness : IAsyncDisposable
{
    private CSharpExpertHarness(UnitBrain brain, CodingRunHost host, ScriptedAgentScript agent, ScriptedProcessScript process, string workspaceRoot)
    {
        Brain = brain;
        Host = host;
        Agent = agent;
        Process = process;
        WorkspaceRoot = workspaceRoot;
    }

    public UnitBrain Brain { get; }

    public CodingRunHost Host { get; }

    public ScriptedAgentScript Agent { get; }

    public ScriptedProcessScript Process { get; }

    public string WorkspaceRoot { get; }

    public static async Task<CSharpExpertHarness> StartAsync(CancellationToken cancellationToken, ScriptedAgentScript? agent = null, ScriptedProcessScript? process = null)
    {
        var agentScript = agent ?? new ScriptedAgentScript();
        var processScript = process ?? new ScriptedProcessScript();
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "csharp-expert-tests", Guid.NewGuid().ToString("N"));
        var brain = await UnitTest.Create()
            .WithModule<CSharpExpertModule>()
            .WithModule<RoslynModule>()
            .WithModule<DotNetModule>()
            .WithExecution(new TestExecutionOptions
            {
                StartupTimeout = TimeSpan.FromMinutes(3),
                AssertionTimeout = TimeSpan.FromSeconds(30),
                CleanupTimeout = TimeSpan.FromSeconds(30),
                PrivateConfiguration = new Dictionary<string, string?>
                {
                    ["DigitalBrain:Coding:SolutionPath"] = CSharpExpertTestHost.SampleSolution,
                    ["DigitalBrain:Coding:WorkspaceKey"] = CodingWorkspace.Id(CSharpExpertTestHost.SampleSolution),
                },
            })
            .ConfigureSilo(silo =>
            {
                silo.Services.Replace(ServiceDescriptor.Singleton<ISolutionLoader>(new SampleSolutionLoader()));
                silo.Services.Replace(ServiceDescriptor.Singleton<IProcessRunner>(new ScriptedProcessRunner(processScript)));
                silo.Services.Replace(ServiceDescriptor.Singleton<ICodingAgentBackend>(new ScriptedCodingBackend(agentScript)));
            })
            .StartAsync(cancellationToken);
        var preparer = new WorkspacePreparer(new ScriptedProcessRunner(processScript), Options.Create(new CSharpExpertModuleOptions { WorkspaceRoot = workspaceRoot }));
        var host = new CodingRunHost(brain, preparer, NullLogger<CodingRunHost>.Instance);
        return new CSharpExpertHarness(brain, host, agentScript, processScript, workspaceRoot);
    }

    public string WorkspaceSolutionPath(string runId)
        => Path.Combine(WorkspaceRoot, runId, Path.GetFileName(CSharpExpertTestHost.SampleSolution));

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Brain.DisposeAsync();
        try
        {
            if (Directory.Exists(WorkspaceRoot))
            {
                Directory.Delete(WorkspaceRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
