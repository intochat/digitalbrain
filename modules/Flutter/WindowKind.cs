using System.Text.Json;
using Brain.Contracts;
using Flutter.Contracts;

namespace Brain.Modules.Flutter;

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
        var document = UiDocument.Parse(invocation.InputJson);

        var output = JsonSerializer.Serialize(new WindowReply(context.Revision + 1), JsonSerializerOptions.Web);
        var events = new[] { ("window.rendered", JsonSerializer.Serialize(document, JsonSerializerOptions.Web)) };

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
