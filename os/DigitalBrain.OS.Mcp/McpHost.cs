namespace DigitalBrain.OS.Mcp;

internal static class McpHost
{
    public const string EndpointPath = "/mcp";
    public const string SendChatMessageToolName = "send_chat_message";
    public const string ListActiveNeuronsToolName = "list_active_neurons";
    public const string ReadNeuronJournalToolName = "read_neuron_journal";
    public const string ReadChatTranscriptToolName = "read_chat_transcript";

    public static WebApplication MapMcpHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapMcp(EndpointPath);
        return app;
    }
}
