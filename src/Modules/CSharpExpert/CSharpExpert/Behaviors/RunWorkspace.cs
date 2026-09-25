namespace DigitalBrain.CSharpExpert;

internal static class RunWorkspace
{
    public static string SolutionPath(CodingRunSnapshot snapshot)
        => snapshot.WorkspaceSolutionPath ?? throw new InvalidOperationException("The run has no prepared workspace yet.");
}
