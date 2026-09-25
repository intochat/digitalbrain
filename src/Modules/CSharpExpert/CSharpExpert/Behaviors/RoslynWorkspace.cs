using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

internal static class RoslynWorkspace
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromMinutes(10);

    public static async Task OpenAsync(IRoslyn roslyn, string solutionPath, CancellationToken cancellation)
    {
        await roslyn.Open(new OpenWorkspace(solutionPath)).ConfigureAwait(false);
        var expected = Path.GetFullPath(solutionPath);
        var deadline = DateTimeOffset.UtcNow + OpenTimeout;
        while (true)
        {
            var snapshot = await roslyn.Read().ConfigureAwait(false);
            var isThisSolution = snapshot.SolutionPath is { } opened
                && string.Equals(Path.GetFullPath(opened), expected, StringComparison.OrdinalIgnoreCase);
            if (isThisSolution && snapshot.Phase == WorkspacePhase.Ready)
            {
                return;
            }

            if (isThisSolution && snapshot.Phase == WorkspacePhase.Failed)
            {
                throw new InvalidOperationException($"Roslyn could not open '{solutionPath}': {snapshot.Detail}");
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Roslyn did not finish opening '{solutionPath}' within {OpenTimeout.TotalMinutes:0} minutes.");
            }

            await Task.Delay(PollInterval, cancellation).ConfigureAwait(false);
        }
    }
}
