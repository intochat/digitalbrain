using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Twitter;

[GrainType("twitter")]
internal sealed class TwitterNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TwitterState>> state)
    : Neuron<TwitterState>(runtime, state), ITwitter
{
    private const int RetentionLimit = 4096;
    private const string Receiving = "TwitterReceiving";

    public Task<Accepted<Posted>> Accept(Post command) => ExecuteCommandAsync(
        Descriptor("accept"), command, TwitterJson.Default.Post, TwitterJson.Default.AcceptedPosted, arguments =>
        {
            if (string.IsNullOrWhiteSpace(arguments.ProviderPostId) || arguments.ProviderPostId.Length > 256 ||
                string.IsNullOrWhiteSpace(arguments.Author) || arguments.Author.Length > 64 ||
                string.IsNullOrWhiteSpace(arguments.Text) || arguments.Text.Length > 25000)
            {
                throw new CommandRejectedException(arguments.Id, "invalid post", "Provide a post ID (1–256 characters), author (1–64) and text (1–25000).");
            }

            if (!string.Equals(Account(arguments.Author), Account(Id.Name), StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandRejectedException(arguments.Id, "author does not match source account", "Send this receipt to the twitter neuron named for its author.");
            }

            var body = new Posted(arguments.ProviderPostId, Account(Id.Name).ToLowerInvariant(), arguments.Text);
            return new Accepted<Posted>(body, Schedule(Signal.FromJson(Receiving, body, TwitterJson.Default.Posted)));
        });

    public Task<TwitterSnapshot> Read() => Task.FromResult(new TwitterSnapshot(
        State?.PublishedCount ?? 0, State?.PostIds.Length ?? 0, State?.LastPost));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Source != Id || delivery.Signal.Type != Receiving || Body(delivery, TwitterJson.Default.Posted) is not { } post)
        {
            return;
        }

        // Only command-scheduled receipts are accepted; a forwarded signal is not provider ingress.
        if (!string.Equals(post.Author, Account(Id.Name), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var retained = State?.PostIds ?? [];
        if (retained.Contains(post.EventId, StringComparer.Ordinal))
        {
            return;
        }

        var nextIds = retained.Length >= RetentionLimit ? retained[1..].Append(post.EventId).ToArray() : retained.Append(post.EventId).ToArray();
        Announce(Signal.FromJson(TwitterModule.PostedSignal, post, TwitterJson.Default.Posted));
        await SaveAsync(new TwitterState(nextIds, (State?.PublishedCount ?? 0) + 1, post), cancellationToken).ConfigureAwait(true);
    }

    private static string Account(string value) => value.Trim().TrimStart('@');
}

[GenerateSerializer]
[Alias("twitter.state")]
internal sealed record TwitterState(
    [property: Id(0)] string[] PostIds,
    [property: Id(1)] long PublishedCount,
    [property: Id(2)] Posted LastPost);
