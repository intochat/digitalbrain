namespace DigitalBrain;

public sealed record DigitalBrainModelConfiguration(
    ChatModelDescriptor Fast,
    ChatModelDescriptor Balanced,
    ChatModelDescriptor Reasoning,
    EmbeddingModelDescriptor Embedding)
{
    public ChatModelDescriptor ForRole(ConversationRole role) =>
        role switch
        {
            ConversationRole.Fast => Fast,
            ConversationRole.Balanced => Balanced,
            ConversationRole.Reasoning => Reasoning,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
}

public sealed class DigitalBrainModelConfigurationBuilder
{
    private ChatModelDescriptor? _fast;
    private ChatModelDescriptor? _balanced;
    private ChatModelDescriptor? _reasoning;
    private EmbeddingModelDescriptor? _embedding;

    public DigitalBrainModelConfigurationBuilder AssignFast(ChatModelDescriptor descriptor)
    {
        _fast = RequireUnassigned(_fast, descriptor, nameof(ConversationRole.Fast));
        return this;
    }

    public DigitalBrainModelConfigurationBuilder AssignBalanced(ChatModelDescriptor descriptor)
    {
        _balanced = RequireUnassigned(_balanced, descriptor, nameof(ConversationRole.Balanced));
        return this;
    }

    public DigitalBrainModelConfigurationBuilder AssignReasoning(ChatModelDescriptor descriptor)
    {
        _reasoning = RequireUnassigned(_reasoning, descriptor, nameof(ConversationRole.Reasoning));
        return this;
    }

    public DigitalBrainModelConfigurationBuilder AssignEmbedding(EmbeddingModelDescriptor descriptor)
    {
        _embedding = RequireUnassigned(_embedding, descriptor, nameof(DigitalBrainModelConfiguration.Embedding));
        return this;
    }

    public DigitalBrainModelConfiguration Build() =>
        new(
            RequireAssigned(_fast, nameof(ConversationRole.Fast)),
            RequireAssigned(_balanced, nameof(ConversationRole.Balanced)),
            RequireAssigned(_reasoning, nameof(ConversationRole.Reasoning)),
            RequireAssigned(_embedding, nameof(DigitalBrainModelConfiguration.Embedding)));

    private static TDescriptor RequireUnassigned<TDescriptor>(
        TDescriptor? current,
        TDescriptor descriptor,
        string role)
        where TDescriptor : ModelDescriptor
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return current is null
            ? descriptor
            : throw new InvalidOperationException($"The {role} model role is already assigned.");
    }

    private static TDescriptor RequireAssigned<TDescriptor>(TDescriptor? descriptor, string role)
        where TDescriptor : ModelDescriptor =>
        descriptor ?? throw new InvalidOperationException($"The {role} model role is not assigned.");
}
