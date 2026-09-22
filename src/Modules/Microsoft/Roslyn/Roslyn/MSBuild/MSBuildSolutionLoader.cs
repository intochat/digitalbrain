using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DigitalBrain.Microsoft.Roslyn;

public sealed class MSBuildSolutionLoader : ISolutionLoader
{
    private static readonly Lock RegistrationGate = new();

    public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(progress);
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution '{solutionPath}' does not exist.", solutionPath);
        }

        EnsureLocatorRegistered();
        return OpenCoreAsync(solutionPath, progress, cancellationToken);
    }

    // The silo registers the locator as its first statement (Program.cs); this guard is for the test
    // process, which has no such entry point. It must run before any Microsoft.Build type is
    // JIT-compiled, so nothing in this method or its callers references one; OpenCoreAsync is kept out
    // of line for the same reason.
    private static void EnsureLocatorRegistered()
    {
        lock (RegistrationGate)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<LoadedSolution> OpenCoreAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create();
        var failures = new List<string>();
        using var registration = workspace.RegisterWorkspaceFailedHandler(args =>
        {
            failures.Add(args.Diagnostic.Message);
            progress.Report(args.Diagnostic.Message);
        });
        try
        {
            await workspace.OpenSolutionAsync(solutionPath,
                new Progress<ProjectLoadProgress>(load => progress.Report($"{load.Operation} {Path.GetFileNameWithoutExtension(load.FilePath)}")),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }

        if (workspace.CurrentSolution.ProjectIds.Count == 0)
        {
            var reason = failures.Count == 0 ? "no projects were loaded" : failures[0];
            workspace.Dispose();
            throw new InvalidOperationException($"Solution '{solutionPath}' loaded no projects: {reason}");
        }

        return new LoadedSolution(workspace, failures);
    }
}