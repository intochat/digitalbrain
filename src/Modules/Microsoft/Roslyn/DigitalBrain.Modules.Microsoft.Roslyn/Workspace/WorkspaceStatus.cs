namespace DigitalBrain.Microsoft.Roslyn;

public sealed record WorkspaceStatus(WorkspacePhase Phase, string? SolutionPath, int ProjectCount, int DocumentCount, string? Detail, bool ReloadNeeded = false)
{
    public static readonly WorkspaceStatus NotOpened = new(WorkspacePhase.NotOpened, null, 0, 0, null);

    public string Advice
    {
        get
        {
            if (ReloadNeeded)
            {
                return "The workspace is ready, but project files changed; reload to pick them up.";
            }

            return Phase switch
            {
                WorkspacePhase.NotOpened => "No solution is open. Open one with the workspace's open command.",
                WorkspacePhase.Opening => $"The solution is still opening ({Detail}). Try again in a moment.",
                WorkspacePhase.Failed => $"The solution failed to open: {Detail}. Fix the cause and reload.",
                _ => "The workspace is ready.",
            };
        }
    }
}