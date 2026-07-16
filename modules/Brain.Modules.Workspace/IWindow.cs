using Brain.Contracts;

namespace Brain.Modules.Workspace;

public interface IWindow : INeuronContract
{
    static string ContractDescription => "Script-writable UI neuron for rendering block documents.";
    [NeuronContract("window.render.v1")]
    Task<WindowReply> RenderAsync(BlockDoc doc);
}

public sealed record WindowReply(long Revision);
