using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class WorkspaceAccessTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>();

    [Fact]
    public void DoesNotExposeAmbientPublisherOrJournalReaderServices()
    {
        Assert.False(HasAmbientAccessServices());
    }

    [Fact]
    public void WorkspaceChannelCannotChooseItsSourceAfterIssuance()
    {
        Assert.NotNull(typeof(WorkspaceChannel).GetProperty(nameof(WorkspaceChannel.Publisher)));
        Assert.DoesNotContain(
            typeof(WorkspaceChannel).GetMethods(),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(SynapseSource)));
    }

    [Fact]
    public async Task ScopeBoundChannelsKeepSameSourceNamesInSeparateJournals()
    {
        const string sourceName = "shared-source";
        var source = new NeuronId("digitalbrain.synapse-source", sourceName);
        var left = OpenWorkspace("workspace/left", sourceName, typeof(MechanicsStart));
        var right = OpenWorkspace("workspace/right", sourceName, typeof(MechanicsStart));

        await left.Publisher.PublishAsync(new MechanicsStart(), Cancellation);
        await right.Publisher.PublishAsync(new MechanicsStart(Echo: true), Cancellation);

        var leftPage = await ReadAsync(left, source, cancellationToken: Cancellation);
        var rightPage = await ReadAsync(right, source, cancellationToken: Cancellation);

        var leftRecord = Assert.Single(leftPage.Records);
        var rightRecord = Assert.Single(rightPage.Records);
        Assert.Equal(1, leftPage.JournalEndPosition);
        Assert.Equal(1, rightPage.JournalEndPosition);
        Assert.False(leftRecord.Serialization.GetProperty("echo").GetBoolean());
        Assert.True(rightRecord.Serialization.GetProperty("echo").GetBoolean());
        Assert.DoesNotContain("workspace", leftRecord.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspace", rightRecord.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
