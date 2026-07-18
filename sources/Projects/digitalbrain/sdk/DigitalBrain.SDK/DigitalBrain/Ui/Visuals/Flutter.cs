using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Ui;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GrainType(NeuronTargetFqn)]
[Neuron]
internal sealed partial class Flutter : Neuron, ICallNeuronTarget, IHandle<RfwCard>
{
    public const string NeuronTargetFqn = "DigitalBrain.UI.Flutter";

    public Task HandleAsync(RfwCard synapse, CancellationToken cancellationToken) => Handle(synapse);
    public Task Handle(RfwCard synapse) => Task.CompletedTask;

    public async Task<string> AskAsync(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return "Usage: render library=<lib> root=<widget> data=<json>";

        Logger.LogInformation("Flutter handling command: {Prompt}", prompt);

        if (prompt.StartsWith("render", StringComparison.OrdinalIgnoreCase))
        {
            var libraryName = ExtractArg(prompt, "library") ?? "dynamic_lib";
            var rootWidget = ExtractArg(prompt, "root") ?? "MainView";
            var dataJson = ExtractArg(prompt, "data") ?? "{}";

            var headers = new SynapseMetadata(
                SynapseId: SynapseId.New(),
                CorrelationId: global::DigitalBrain.Runtime.Neurons.CorrelationId.New(),
                CausationId: null,
                CallerNeuronId: new NeuronId(InstanceId.ToString()),
                CallerNeuronType: NeuronType,
                ReceiverNeuronId: new NeuronId(Guid.Empty.ToString()),
                ReceiverNeuronType: "External",
                Timestamp: DateTimeOffset.UtcNow
            );

            var rfwCard = new RfwCard(
                LibraryName: libraryName,
                RootWidget: rootWidget,
                DataJson: dataJson
            ) { Headers = headers };

            await FireSynapseAsync(rfwCard);
            return "ok";
        }

        if (prompt.StartsWith("hotreload", StringComparison.OrdinalIgnoreCase))
        {
            return "hot reload success";
        }

        if (prompt.StartsWith("compose", StringComparison.OrdinalIgnoreCase))
        {
            return "composition success";
        }

        return $"Unknown command: {prompt}";
    }

    private static string? ExtractArg(string prompt, string key)
    {
        var pattern = $"{key}=";
        var idx = prompt.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx == -1) return null;

        var start = idx + pattern.Length;
        if (start >= prompt.Length) return "";

        if (prompt[start] == '"')
        {
            var end = prompt.IndexOf('"', start + 1);
            return end == -1 ? prompt.Substring(start + 1) : prompt.Substring(start + 1, end - start - 1);
        }
        else
        {
            var end = prompt.IndexOf(' ', start);
            return end == -1 ? prompt.Substring(start) : prompt.Substring(start, end - start);
        }
    }
}
