namespace DigitalBrain.Coding;

// dotnet build of the loaded solution into the slot's own output root, plus the verdict a rollback needs:
// did this landing write a file that declares a [GenerateSerializer] type?
public sealed class SlotBuilder(DotnetRunner dotnet, SolutionWorkspace workspace, ChangeSetEditor editor, SlotOptions options) : ISlotBuilder
{
    public async Task<SlotBuildResult> BuildAsync(string slot, IReadOnlyList<string> changedFiles, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(changedFiles);
        var status = workspace.Status;
        var solutionPath = status.SolutionPath ?? throw new WorkspaceNotReadyException(status);
        var artifacts = options.ArtifactsFor(slot, Path.GetDirectoryName(Path.GetFullPath(solutionPath))!);
        var build = await dotnet.BuildAsync(solutionPath, artifacts, cancellationToken).ConfigureAwait(false);
        var touches = changedFiles.Count > 0
            && await workspace.QueryAsync(
                (solution, token) => editor.TouchesSerializedStateAsync(solution, changedFiles, token),
                cancellationToken).ConfigureAwait(false);
        return new SlotBuildResult(build, artifacts, touches);
    }
}
