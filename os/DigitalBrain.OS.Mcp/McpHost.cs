namespace DigitalBrain.OS.Mcp;

internal static class McpHost
{
    public const string EndpointPath = "/mcp";
    public const string AskLlama32ToolName = "ask_llama32";
    public const string DefaultLlama32Key = "default";
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
