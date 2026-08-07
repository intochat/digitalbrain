using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class WorkspaceServiceTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>()
            .RegisterWorkspaceService<IWorkspaceMarker>(static workspace => new WorkspaceMarker(
                workspace.Id switch
                {
                    "workspace/left" => "left",
                    "workspace/right" => "right",
                    _ => throw new InvalidOperationException($"Unexpected workspace binding '{workspace.Id}'."),
                }))
            .RegisterNeuron<WorkspaceMarkerProbe>("workspace-marker-probe");

    [Fact]
    public async Task WorkspaceBoundProviderChangesWithThePhysicalWorkspace()
    {
        const string sourceName = "origin";
        var probe = new NeuronId("workspace-marker-probe", sourceName);
        var left = OpenWorkspace("workspace/left", sourceName, typeof(MechanicsStart));
        var right = OpenWorkspace("workspace/right", sourceName, typeof(MechanicsStart));

        await left.Publisher.PublishAsync(new MechanicsStart(), Cancellation);
        await right.Publisher.PublishAsync(new MechanicsStart(Echo: true), Cancellation);

        var leftPage = await WaitForJournalAsync(
            left,
            probe,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WorkspaceMarkerObserved).FullName),
            "the left workspace marker",
            Cancellation);
        var rightPage = await WaitForJournalAsync(
            right,
            probe,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WorkspaceMarkerObserved).FullName),
            "the right workspace marker",
            Cancellation);

        Assert.Equal("left", MarkerOf(leftPage));
        Assert.Equal("right", MarkerOf(rightPage));
    }

    private static string? MarkerOf(JournalPage page)
        => page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WorkspaceMarkerObserved).FullName)
            .Serialization.GetProperty("label").GetString();

    private sealed class WorkspaceMarker(string label) : IWorkspaceMarker
    {
        public string Label { get; } = label;
    }
}
