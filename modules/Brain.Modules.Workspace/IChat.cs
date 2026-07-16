using Brain.Contracts;

namespace Brain.Modules.Workspace;

public interface IChat : INeuronContract
{
    static string ContractDescription => "Owner-scoped conversation neuron.";
    [NeuronContract("chat.post.v1")]
    Task<ChatPostReply> PostAsync(ChatPost post);
}

public sealed record ChatPost(string Text);
public sealed record ChatPostReply(long Revision);
