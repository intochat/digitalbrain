using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using System.ClientModel;

namespace DigitalBrain.Kernel;

internal sealed class FastModelClient(IChatClient client)
{
    public IChatClient Client { get; } = client;
}

internal sealed class BalancedModelClient(IChatClient client)
{
    public IChatClient Client { get; } = client;
}

internal sealed class ReasoningModelClient(IChatClient client)
{
    public IChatClient Client { get; } = client;
}

internal sealed class EmbeddingModelClient(
    IEmbeddingGenerator<string, Embedding<float>> client)
{
    public IEmbeddingGenerator<string, Embedding<float>> Client { get; } = client;
}

internal interface IConversationRoleInvoker
{
    Task<string> CompleteAsync(
        ConversationRole role,
        string text,
        CancellationToken cancellationToken);
}

internal sealed class ProviderInvocationException(
    bool outcomeUnknown,
    string message,
    Exception innerException)
    : Exception(message, innerException)
{
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

internal sealed class ConversationRoleInvoker(
    FastModelClient fast,
    BalancedModelClient balanced,
    ReasoningModelClient reasoning)
    : IConversationRoleInvoker
{
    public async Task<string> CompleteAsync(
        ConversationRole role,
        string text,
        CancellationToken cancellationToken)
    {
        var client = role switch
        {
            ConversationRole.Fast => fast.Client,
            ConversationRole.Balanced => balanced.Client,
            ConversationRole.Reasoning => reasoning.Client,
            _ => throw new BrainException(
                NeuronFailureKind.OperationFailed,
                "A declared conversation role is required.")
        };

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, text)],
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ClientResultException { Status: >= 400 } ||
            exception is AnthropicApiException { StatusCode: >= System.Net.HttpStatusCode.BadRequest })
        {
            throw new ProviderInvocationException(
                outcomeUnknown: false,
                "The provider rejected the request.",
                exception);
        }
        catch (Exception exception)
        {
            throw new ProviderInvocationException(
                outcomeUnknown: true,
                "The provider outcome is unknown.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(response.Text))
            throw new ProviderInvocationException(
                outcomeUnknown: true,
                "The provider outcome is unknown.",
                new InvalidOperationException("The provider returned no text response."));

        return response.Text;
    }
}
