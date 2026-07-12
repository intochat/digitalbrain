using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Ui;

using DigitalBrain.Ui.Contracts;
using DigitalBrain.Ui.Contracts.Ui;

// The Chat neuron (server-driven UI). On a data-visualization request it emits an RfwCard, journaled as
// conversation history.
[GrainType("digitalbrain.chat.v1")]
public class ChatNeuron(ILogger<ChatNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IChatNeuron
{
    public async Task HandleAsync(VisualizeDataRequest request, CancellationToken cancellationToken = default)
    {
        var dataJson = JsonSerializer.Serialize(new
        {
            prompt = request.Prompt,
            data = request.DataJson,
            chartHint = request.ChartHint
        });
        var card = new RfwCard("digitalbrain", "DataChartCard", dataJson);

        await FireAsync(card, cancellationToken);  // keep for conversation journal compat
    }

    public Task<RfwCard[]> GetConversationAsync()
        => Task.FromResult(OutgoingJournal.OfType<RfwCard>().ToArray());
}


