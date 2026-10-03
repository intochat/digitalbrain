using DigitalBrain.AI.Agents;

namespace DigitalBrain.Assistant;

// The host supplies product policy. The neuron adds workspace tools and
// retained conversation context before executing a turn.
public static class AssistantDefinition
{
    private static readonly string NeuronInstructions = ReadInstructions();
    public static AgentDefinition Product { get; } = For();

    public static AgentDefinition For(AssistantOptions? options = null) => new()
    {
        DisplayName = options?.DisplayName ?? "Workspace assistant",
        Instructions = NeuronInstructions + "\n" + options?.Instructions,
        Tools = ["discover_capabilities", "table_read", "table_refine", "show_form", "show_view"],
    };

    private static string ReadInstructions()
    {
        using var stream = typeof(AssistantDefinition).Assembly.GetManifestResourceStream("instructions.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
