namespace DigitalBrain.Coding;

// A seam, so a slot fact can build a slot without a solution on disk and without dotnet.
public interface ISlotBuilder
{
    Task<SlotBuildResult> BuildAsync(string slot, IReadOnlyList<string> changedFiles, CancellationToken cancellationToken = default);
}
