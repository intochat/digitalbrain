using DigitalBrain;
using Xunit;

namespace DigitalBrain.Tests.AI;

public sealed class ModelRoleConfigurationTests
{
    [Fact]
    public void Complete_configuration_builds_an_immutable_snapshot()
    {
        var configuration = new DigitalBrainModelConfigurationBuilder()
            .AssignFast(new GptFast())
            .AssignBalanced(new ClaudeBalanced())
            .AssignReasoning(new GptReasoning())
            .AssignEmbedding(new TextEmbedding())
            .Build();

        Assert.IsType<GptFast>(configuration.Fast);
        Assert.IsType<ClaudeBalanced>(configuration.Balanced);
        Assert.IsType<GptReasoning>(configuration.Reasoning);
        Assert.IsType<TextEmbedding>(configuration.Embedding);
        Assert.Equal(
            new GptFast(),
            configuration.ForRole(ConversationRole.Fast));
        Assert.Equal(
            new ClaudeBalanced(),
            configuration.ForRole(ConversationRole.Balanced));
        Assert.Equal(
            new GptReasoning(),
            configuration.ForRole(ConversationRole.Reasoning));
    }

    [Fact]
    public void Duplicate_role_assignment_fails_deterministically()
    {
        var builder = new DigitalBrainModelConfigurationBuilder().AssignFast(new GptFast());

        var duplicate = Assert.Throws<InvalidOperationException>(() => builder.AssignFast(new GptFast()));

        Assert.Contains(nameof(ConversationRole.Fast), duplicate.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConversationRole.Fast)]
    [InlineData(ConversationRole.Balanced)]
    [InlineData(ConversationRole.Reasoning)]
    public void Missing_role_assignment_fails_deterministically(ConversationRole missingRole)
    {
        var builder = new DigitalBrainModelConfigurationBuilder();
        if (missingRole != ConversationRole.Fast)
            builder.AssignFast(new GptFast());
        if (missingRole != ConversationRole.Balanced)
            builder.AssignBalanced(new ClaudeBalanced());
        if (missingRole != ConversationRole.Reasoning)
            builder.AssignReasoning(new GptReasoning());
        builder.AssignEmbedding(new TextEmbedding());

        var missing = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(missingRole.ToString(), missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_embedding_assignment_fails_deterministically()
    {
        var builder = new DigitalBrainModelConfigurationBuilder()
            .AssignFast(new GptFast())
            .AssignBalanced(new ClaudeBalanced())
            .AssignReasoning(new GptReasoning());

        var missing = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("Embedding", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedding_descriptor_cannot_fill_a_chat_role()
    {
        var configurationType = typeof(DigitalBrainModelConfigurationBuilder);

        foreach (var assignment in new[] { "AssignFast", "AssignBalanced", "AssignReasoning" })
        {
            var parameter = Assert.Single(configurationType.GetMethod(assignment)!.GetParameters());
            Assert.Equal(typeof(ChatModelDescriptor), parameter.ParameterType);
        }

        var embeddingParameter = Assert.Single(configurationType.GetMethod("AssignEmbedding")!.GetParameters());
        Assert.Equal(typeof(EmbeddingModelDescriptor), embeddingParameter.ParameterType);
    }
}
