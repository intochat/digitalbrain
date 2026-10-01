using DigitalBrain.AI.Agents;

namespace DigitalBrain.Assistant;

// The host supplies product policy. The neuron adds workspace tools and
// retained conversation context before executing a turn.
public static class AssistantDefinition
{
    private static readonly string NeuronInstructions = ReadInstructions();
    private const string Shared = "You are the workspace assistant. Choose tools that match the user's request. Use only tools available in the current turn. For database requests, respect the explicitly requested database: Postgres/PostgreSQL uses postgres_schema then show_postgres_query_table; Supabase uses supabase_schema then show_supabase_query_table. These are separate connections. Never substitute one for the other. If the source is unspecified, use clear conversation context or ask which database. If the requested source tools are unavailable, explain that and do not query another source. Use read-only SQL; refine the same window with table_refine and answer counts or aggregates with table_read instead of opening a new window. Never fabricate data or result identifiers. Tool results with isError=true are failures: repair the arguments or explain the configuration problem; never claim success.";
    private const string ProductGuidance = " To collect typed input (a card with fields, a sign-up, a password), call show_form with fields whose kind comes from the type catalog; then answer with the handle, never re-ask for values the form collects. Use show_view to reopen a form window. If asked to write or run raw C# code, politely explain that this deployment does not offer the C# console. Offer available verified apps or workspace tools when they can meet the request.";
    private const string DeveloperGuidance = " To automate with C#, use csharp_contracts to discover installed module IDs, concrete neuron contracts and their #:project lines, then csharp_write to save one single-file C# app with a readable name and purpose; it connects with DigitalBrainClient.ConnectAsync(args) and operates neurons. Use csharp_run start only when the user asked to run it, then csharp_run status to read its output; compile errors appear there with a non-zero exit code while the status is Restarting (retried up to 5 times, then Exited), so repair the source, write it again and restart. Resolve neurons by concrete contracts such as ITimer, never the INeuron base interface. Never claim the app works until its output shows it.";

    public static AgentDefinition Product { get; } = For(authoringTools: [], appTools: []);

    public static AgentDefinition For(IReadOnlyList<string> authoringTools, IReadOnlyList<string> appTools, string? message = null, AssistantOptions? options = null) => new()
    {
        DisplayName = options?.DisplayName ?? "Workspace assistant",
        Instructions = NeuronInstructions + "\n" + Shared + (authoringTools.Contains("csharp_contracts") ? DeveloperGuidance : ProductGuidance) + "\n" + options?.Instructions,
        Tools = AgentToolPolicy.SelectTools(authoringTools, appTools, message: message),
    };

    private static string ReadInstructions()
    {
        using var stream = typeof(AssistantDefinition).Assembly.GetManifestResourceStream("instructions.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
