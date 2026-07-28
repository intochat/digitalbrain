using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

[Alias("DigitalBrain.ModuleTests.ICountingStreamProbe")]
[ClientEntryPoint]
public partial interface ICountingStreamProbe : INeuron
{
    [Alias(nameof(CountUp))]
    IAsyncEnumerable<int> CountUp(int elements);

    [Alias(nameof(DrainAnotherOwnersProbe))]
    Task DrainAnotherOwnersProbe(string foreignOwner, string probeName, int elements);
}

public sealed class CountingStreamProbe : Neuron, ICountingStreamProbe
{
    public async IAsyncEnumerable<int> CountUp(int elements)
    {
        for (var element = 0; element < elements; element++)
        {
            yield return await Task.FromResult(element);
        }
    }

    public async Task DrainAnotherOwnersProbe(string foreignOwner, string probeName, int elements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(foreignOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(probeName);

        var foreign = GrainFactory.GetGrain<ICountingStreamProbe>(
            NeuronId.For<ICountingStreamProbe>(new OwnerId(foreignOwner), probeName).ToGrainId());

        await foreach (var _ in foreign.CountUp(elements))
        {
        }
    }
}

public sealed class StreamedClientAuthorization(ModuleFixture fixture)
{
    private const string ProbeName = "streamed-client-probe";
    private const string CallerName = "streamed-client-caller";
    private const string ForeignOwnerLabel = "streamed-client-foreign";
    private const int ElementsSpanningSeveralBatches = 250;
    private const int ReducedBatchSize = 7;

    [Fact(DisplayName = "an unattributed client receives a stream spanning several MoveNext batches complete and in order")]
    public async Task ClientReceivesAMultiBatchStreamCompleteAndInOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var received = new List<int>();

        await foreach (var element in test.Client.Get<ICountingStreamProbe>(ProbeName)
            .CountUp(ElementsSpanningSeveralBatches)
            .WithCancellation(cancellationToken))
        {
            received.Add(element);
        }

        Assert.Equal(Enumerable.Range(0, ElementsSpanningSeveralBatches), received);
    }

    [Fact(DisplayName = "the same client stream at a reduced batch size arrives complete and in order")]
    public async Task ReducedBatchSizeStreamArrivesCompleteAndInOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var received = new List<int>();

        await foreach (var element in test.Client.Get<ICountingStreamProbe>(ProbeName)
            .CountUp(ElementsSpanningSeveralBatches)
            .WithBatchSize(ReducedBatchSize)
            .WithCancellation(cancellationToken))
        {
            received.Add(element);
        }

        Assert.Equal(Enumerable.Range(0, ElementsSpanningSeveralBatches), received);
    }

    [Fact(DisplayName = "an unattributed client's ILLM-shaped stream against a neuron without that contract is refused")]
    public async Task ClientEntryPointShapedStreamAgainstAForeignContractIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var imposter = test.Cluster.Client.GetGrain<ILLM>(
            NeuronId.For<ICountingStreamProbe>(test.Client.Owner, ProbeName).ToGrainId());

        await Assert.ThrowsAsync<NeuronAuthorizationException>(async () =>
        {
            await foreach (var _ in imposter
                .RespondStreaming([new ChatMessage(ChatRole.User, "hi")], cancellationToken))
            {
            }
        });
    }

    [Fact(DisplayName = "a neuron streaming from another owner's neuron is still refused")]
    public async Task StreamedCallAcrossOwnersIsStillRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var foreignOwner = test.Owner(ForeignOwnerLabel);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => test.Client.Get<ICountingStreamProbe>(CallerName)
                .DrainAnotherOwnersProbe(foreignOwner.Id.Value, ProbeName, elements: 3));
    }
}
