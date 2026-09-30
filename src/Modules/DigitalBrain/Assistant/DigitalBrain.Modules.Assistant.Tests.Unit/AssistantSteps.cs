using System.Globalization;
using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.VoiceInput;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Specs;
using DigitalBrain.Supabase.Tables;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant.Tests.Unit;

// The subject of a run is the key the assistant is started for.
internal sealed class AssistantSteps : StepLibrary
{
    private static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(10);

    public override string Name => "Assistant";

    public AssistantSteps()
    {
        Step("the assistant is started", "Starts the assistant app for the subject key.", context =>
            context.Grains.GetGrain<IAssistant>(context.Subject).Activate());
        Step("its surface is titled {string}", "The assistant's surface shows this title.", async (context, args) =>
        {
            var title = (await Surface(context).Read()).Definition.Title;
            if (title != args.Text(0)) { throw new StepFailedException($"The surface is titled \"{title}\"."); }
        });
        Step("its surface declares explicit assistant controls", "The app declares the full control tree using generic primitives.", context =>
            Ui(context).AssertExplicitControls());
        Step("I draft {string}", "Edits the bound multiline field.", (context, args) => Ui(context).Input(args.Text(0)));
        Step("the chat draft is {string}", "Reopening has not erased the draft.", async (context, args) =>
        {
            await Ui(context).WaitForDraft(args.Text(0));
        });
        Step("conversation {string} has a completed answer {string}", "Keeps a completed turn in the app-owned conversation.", async (context, args) =>
        {
            var conversation = await Assistant(context).Conversation(args.Text(0));
            await conversation.BeginConversation(new("run-1", "question"), context.CancellationToken);
            await conversation.CompleteConversation(new("run-1", "question", args.Text(1), []), context.CancellationToken);
        });
        Step("conversation {string} contains the answer {string}", "Reads a saved turn after reopening.", async (context, args) =>
        {
            var conversation = await Assistant(context).Conversation(args.Text(0));
            var turns = (await conversation.ReadConversation(context.CancellationToken)).Turns;
            if (turns.Count != 1 || turns[0].AssistantText != args.Text(1)) { throw new StepFailedException("The saved turn is missing or duplicated."); }
        });
        Step("conversation {string} is empty", "New threads do not inherit another thread's history.", async (context, args) =>
        {
            var conversation = await Assistant(context).Conversation(args.Text(0));
            if ((await conversation.ReadConversation(context.CancellationToken)).Turns.Count != 0)
            { throw new StepFailedException("History leaked across threads."); }
        });
        Step("another workspace has no turns in conversation {string}", "Workspace conversations remain isolated.", async (context, args) =>
        {
            var other = context.Grains.GetGrain<IAssistant>(AssistantSurface.Key(context.Subject.Split("/applications/")[0] + "-other"));
            var conversation = await other.Conversation(args.Text(0));
            if ((await conversation.ReadConversation(context.CancellationToken)).Turns.Count != 0)
            { throw new StepFailedException("History leaked across workspaces."); }
        });
        Step("I configure the assistant instructions as {string}", "Configures the app-owned agent.", (context, args) =>
            Assistant(context).Configure(new AgentDefinition { DisplayName = "Custom assistant", Instructions = args.Text(0) }));
        Step("the assistant turn uses instructions {string}", "The application preserves its configured agent.", async (context, args) =>
        {
            if ((await Assistant(context).DefineTurn(false, null)).Instructions != args.Text(0))
            { throw new StepFailedException("The application lost its agent configuration."); }
        });
        Step("the assistant includes the summary {string} in its turn instructions", "The neuron composes retained context.", async (context, args) =>
        {
            var definition = await Assistant(context).DefineTurn(false, args.Text(0));
            if (!definition.Instructions.Contains("Earlier conversation summary: " + args.Text(0), StringComparison.Ordinal))
            { throw new StepFailedException("The retained context is missing."); }
        });
        Step("invalid conversation identifiers are rejected", "Rejects keys that could escape the thread scope.", async context =>
        {
            foreach (var invalid in new[] { "", "foreign/thread", "foreign\\thread", new string('x', 201) })
            {
                try { await Assistant(context).Conversation(invalid); }
                catch (ArgumentException) { continue; }
                throw new StepFailedException("An invalid conversation identifier was accepted.");
            }
        });
        Step("I type {string}", "Types into the field and clicks the bound send button.", (context, args) =>
            Ui(context).Submit(args.Text(0)));
        Step("I send the draft", "Clicks the bound send button.", context => Ui(context).Submit());
        Step("I say {string} by voice", "Records audio that the transcriber hears as this text.", (context, args) =>
            context.Grains.GetGrain<IVoiceInput>(UiParts.NameOf(context.Subject, AssistantSurface.VoicePart))
                .Capture(RecordedText(args.Text(0)), "audio/wav"));
        Step("the chat shows my message {string}", "The chat shows this message from the user.", (context, args) =>
            Shows(context, ChatRole.User, args.Text(0)));
        Step("the chat shows the assistant reply {string}", "The chat shows this reply from the assistant.", (context, args) =>
            Shows(context, ChatRole.Assistant, args.Text(0)));
        Step("Supabase has a table {string} with {int} rows", "The Supabase database holds a table with this many rows.", (context, args) =>
        {
            var database = context.Services.GetRequiredService<CustomersDatabase>();
            database.Table = args.Text(0);
            database.Rows = [.. Enumerable.Range(1, args.Int(1)).Select(id => new SupabaseTableRow("row-" + id, [id.ToString(CultureInfo.InvariantCulture), $"\"Customer {id}\""]))];
            return Task.CompletedTask;
        });
        Step("the assistant used {string} and {string}", "The assistant's agent completed calls to both registered tools.", (context, args) =>
        {
            var completed = context.Services.GetRequiredService<InjectedModelTurnRunner>().CompletedTools.ToArray();
            foreach (var method in new[] { args.Text(0), args.Text(1) })
            {
                if (!completed.Contains(method, StringComparer.Ordinal))
                { throw new StepFailedException($"No completed call to {method} among [{string.Join(", ", completed)}]."); }
            }
            return Task.CompletedTask;
        });
        Step("a table window is open in the workspace", "The workspace shows an open table window.", FindTableWindow);
        Step("the table has {int} rows", "The table in the workspace window serves this many rows.", async (context, args) =>
        {
            var window = await FindTableWindow(context);
            var rows = (await context.Grains.GetGrain<ISupabaseTable>(window.Reference.NeuronId).Read(new(0, 50)))?.Rows.Count ?? 0;
            if (rows != args.Int(0)) { throw new StepFailedException($"The table has {rows} rows."); }
        });
    }

    private static async Task<WorkspaceWindow> FindTableWindow(StepContext context)
    {
        var workspace = context.Grains.GetGrain<IWorkspace>(context.Subject.Split("/applications/")[0]);
        return (await workspace.Read()).Windows.FirstOrDefault(window => window.IsOpen && window.Reference.Kind == WindowReference.TableKind)
            ?? throw new StepFailedException("No table window is open in the workspace.");
    }

    private void Step(string pattern, string description, Func<StepContext, Task> run) => Step(pattern, description, (context, _) => run(context));

    private static IAssistant Assistant(StepContext context) => context.Grains.GetGrain<IAssistant>(context.Subject);

    private static ISurface Surface(StepContext context) => context.Grains.GetGrain<ISurface>(UiParts.NameOf(context.Subject, "surface"));

    private static AssistantUiProbe Ui(StepContext context) => new(context.Grains, context.Subject);

    private static byte[] RecordedText(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var wav = new byte[44 + payload.Length];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        payload.CopyTo(wav, 44);
        return wav;
    }

    // The assistant answers one-way, so the chat is read until the message arrives.
    private static async Task Shows(StepContext context, ChatRole role, string text)
    {
        var deadline = DateTimeOffset.UtcNow + ChatTimeout;
        var ui = Ui(context);
        while (true)
        {
            var thread = await ui.Read();
            if (thread.Messages.Any(message => message.Role == role && message.Text == text)
                && (await ui.VisibleText()).Contains(text, StringComparison.Ordinal)) { return; }
            if (thread.Error is not null || DateTimeOffset.UtcNow > deadline)
            { throw new StepFailedException($"State: {System.Text.Json.JsonSerializer.Serialize(thread)}. Runner: {string.Join("; ", context.Services.GetRequiredService<InjectedModelTurnRunner>().Failures)}."); }
            await Task.Delay(TimeSpan.FromMilliseconds(50), context.CancellationToken);
        }
    }
}