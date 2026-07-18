using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.SDK.Telegram;

[ImplicitStreamSubscription(TelegramAlertNeuronType)]
internal sealed class TelegramAlertNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IHttpClientFactory httpClientFactory,
    ILogger<TelegramAlertNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      INeuronMetadata,
      IExternalNeuron,
      IHandle<SendTelegramAlertRequest>
{
    [global::DigitalBrain.Runtime.Neurons.State.NeuronSetting("Telegram:BotToken", isPrivate: true)]
    private string BotToken { get; set; } = "";

    public const string TelegramAlertNeuronType = nameof(TelegramAlertNeuron);

    public static NeuronId Id => new("google/telegram-alert");
    public static string Icon => "telegram";
    public static NeuronCapability Capabilities => NeuronCapability.External;

    public async Task HandleAsync(SendTelegramAlertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Processing SendTelegramAlertRequest for chatId {ChatId}", request.ChatId);

            var token = BotToken;

            if (string.IsNullOrEmpty(token) || token == "mock-token" || token == "MOCK_TOKEN")
            {
                logger.LogInformation("[MOCK TELEGRAM DISPATCH] Token is empty or mock. Message: {Message}", request.Message);
                Counter("alerts_sent").instrument.Add(1);
                return;
            }

            try
            {
                var client = httpClientFactory.CreateClient();
                var url = $"https://api.telegram.org/bot{token}/sendMessage";
                var payload = new { chat_id = request.ChatId, text = request.Message };

                var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Successfully sent Telegram alert to {ChatId}", request.ChatId);
                    Counter("alerts_sent").instrument.Add(1);
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Failed to send Telegram alert to {ChatId}. Status: {StatusCode}, Error: {Error}", 
                        request.ChatId, response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception while sending Telegram alert to {ChatId}", request.ChatId);
            }
        }
        finally
        {
            await FireSynapseAsync(request with
            {
                ReceiverNeuronType = request.CallerNeuronType ?? "External",
                ReceiverNeuronId = request.CallerNeuronId
            });
        }
    }
}
