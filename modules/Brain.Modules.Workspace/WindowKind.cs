using System.Text.Json;
using Brain.Contracts;

namespace Brain.Modules.Workspace;

public sealed class WindowKind : INeuronKind
{
    public string Kind => "window";
    public string[] Contracts => ["window.render.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "window.render.v1" => HandleRenderAsync(context, invocation),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    private ValueTask<KindResult> HandleRenderAsync(NeuronContext context, NeuronInvocation invocation)
    {
        var doc = BlockDoc.Parse(invocation.InputJson);

        var output = JsonSerializer.Serialize(new { revision = context.Revision + 1 });
        var events = new[] { ("window.rendered", doc.Json) };

        return ValueTask.FromResult(new KindResult(output, events));
    }

    public string Project(NeuronContext context, string projection)
    {
        var latestEvent = context.Journal
            .LastOrDefault(evt => evt.Kind == "window.rendered");

        if (latestEvent != null)
            return latestEvent.PayloadJson;

        return """{"version":1,"blocks":[]}""";
    }
}
