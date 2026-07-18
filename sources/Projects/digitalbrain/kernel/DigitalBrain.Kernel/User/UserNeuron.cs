using DigitalBrain.Runtime.User;
using DigitalBrain.Kernel.Conversation;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Streams;
using DigitalBrain.SDK.DigitalBrain.Ai;

namespace DigitalBrain.Kernel.User;

public sealed class UserNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<UserNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      IUserNeuron, INeuronMetadata
{
    public static NeuronId         Id           => new("kernel/user");
    public static string           Icon         => "user";
    public static NeuronCapability Capabilities => NeuronCapability.None;

    public async Task SubmitPromptAsync(string text, Guid correlationId, CancellationToken ct)
    {
        var userId = this.GetPrimaryKeyString();
        var conversation = Grains.GetGrain<IConversation>(userId);
        await conversation.AppendUserMessageAsync(Guid.NewGuid(), text, correlationId, ct);

        var receiverNeuronType = AiNeuronTypes.IntentNeuron;
        if (text.Contains("Microsoft.Windows.CreateFolder", StringComparison.OrdinalIgnoreCase))
        {
            receiverNeuronType = "NavigatorNeuron";
        }

        await FireSynapseAsync(new UserPromptReceived(UserId:             userId,
        Text:               text) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: null,
            callerNeuronId: StreamKeys.StringKeyToGuid(userId),
            callerNeuronType: nameof(UserNeuron),
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: receiverNeuronType,
            timestamp: time.GetUtcNow()
        ) }, ct);
    }

    public async Task<IReadOnlyList<Guid>> GetRecentCorrelationIdsAsync(TimeSpan since, CancellationToken ct)
    {
        var userId = this.GetPrimaryKeyString();
        var cutoff = time.GetUtcNow() - since;
        var conversation = Grains.GetGrain<IConversation>(userId);
        var messages = await conversation.SinceAsync(cutoff, ct);
        return [.. messages
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.CorrelationId)
            .Distinct()];
    }

    protected override Task HandleSynapseAsync(Synapse s) => Task.CompletedTask;
}