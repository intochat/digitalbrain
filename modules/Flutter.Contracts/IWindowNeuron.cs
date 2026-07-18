using Brain.Contracts;

namespace Flutter.Contracts;

public interface IWindowNeuron : INeuronContract
{
    [NeuronContract("window.render.v1")]
    Task<WindowReply> RenderAsync(UiDocument document);
}

public sealed record WindowReply(long Revision);
