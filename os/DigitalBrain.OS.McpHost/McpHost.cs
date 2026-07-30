namespace DigitalBrain.OS.McpHost;

internal static class McpHost
{
    public const string EndpointPath = "/mcp";
    public const string SendChatMessageToolName = "send_chat_message";
    public const string ListActiveNeuronsToolName = "list_active_neurons";
    public const string ReadNeuronJournalToolName = "read_neuron_journal";
    public const string ReadChatTranscriptToolName = "read_chat_transcript";
    public const string ReadBehaviorToolName = "read_behavior";
    public const string ProposeBehaviorRevisionToolName = "propose_behavior_revision";
    public const string RunBehaviorTestsToolName = "run_behavior_tests";
    public const string ApproveBehaviorRevisionToolName = "approve_behavior_revision";


    public static WebApplication MapMcpHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapMcp(EndpointPath);
        return app;
    }
}
