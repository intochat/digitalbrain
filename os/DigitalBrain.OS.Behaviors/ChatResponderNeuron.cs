using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Chat;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.OS;

[GrainType("os-chat-responder")]
internal sealed class ChatResponderNeuron :
    Neuron,
    IBehavior,
    IHandle<UserMessaged>
{
    private const string StateName = "os.chat-responder.state";
    private const string DeclaredProgramRevision = "1";

    private static readonly ChatResponder Program = new();
    private static readonly BehaviorId Identity = new("com.digitalbrain.chat-responder");
    private static readonly BehaviorRevisionId Revision =
        RevisionOf($"{Identity.Value}@{DeclaredProgramRevision}");

    private readonly IDurableDictionary<string, byte[]> _state;

    public ChatResponderNeuron()
        => _state = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>(StateName);

    public async Task HandleAsync(UserMessaged synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        if (synapse.Chat.Owner != Id.Owner)
        {
            return;
        }

        var context = new PreRailBehaviorContext(
            GrainFactory,
            new BehaviorExecutionMetadata(Id.Owner, Identity, Revision, BehaviorExecutionId.New()),
            TimeProvider,
            _state);

        var answer = await Program.ExecuteAsync(synapse, context, cancellationToken);

        await SendAsync(synapse.Chat, new AssistantAnswered(context.DeterministicCommandId(synapse.CommandId.ToString()), answer));

        await WriteStateAsync(CancellationToken.None);
    }

    private static BehaviorRevisionId RevisionOf(string seed)
        => new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed))));
}
