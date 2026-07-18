using System.Runtime.CompilerServices;
using Brain.Contracts;
using DigitalBrain.AI;
using Microsoft.Extensions.AI;

namespace Brain.AgentGateway;

public sealed class GroupChatNeuronChatClient(IClusterClient clusterClient) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var topic = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? "discussion";
        var brain = new Brain.Client.Brain(clusterClient);
        var org = new OrganizationId("dev-organization");
        var space = new SpaceId("dev-space");
        var chat = brain.Get<IGroupChat>(org, space, "dev-group-chat");
        var gpt = brain.Get<IGpt56>(org, space, "dev-gpt");
        var grok = brain.Get<IGrok45>(org, space, "dev-grok");
        var commandId = Guid.NewGuid();
        var source = new NeuronAddress(org, space, "chat.group.v1", "dev-group-chat");
        var metadata = new SynapseMetadata(
            commandId,
            commandId,
            commandId,
            commandId,
            org,
            new PrincipalId("dev-principal"),
            space,
            source,
            0,
            0,
            DateTimeOffset.UtcNow);
        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            metadata,
            new StartDiscussion(
                topic,
                ((Orleans.Runtime.IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((Orleans.Runtime.IAddressable)grok).GetGrainId().Key.ToString()!)));
        var surface = await chat.GetSurfaceAsync();
        var text = string.Join(
            "\n",
            surface.Surface.Blocks.Select(block => block.Text));
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate(message.Role, message.Contents);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
