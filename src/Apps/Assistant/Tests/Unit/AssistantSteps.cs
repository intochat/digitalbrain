using System.Globalization;
using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.VoiceInput;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Specs;
using DigitalBrain.Supabase.Tables;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Apps.Assistant.Tests.Unit;

// The subject of a run is the key the assistant is started for.
internal sealed class AssistantSteps : StepLibrary
{
    private static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(10);

    public override string Name => "Assistant";

    public AssistantSteps()
    {
        Step("the assistant is started", "Starts the assistant app for the subject key.", context =>
            context.Services.GetRequiredService<ApplicationCatalog>().Start(AppDefinition.NameOf<AssistantApp>(), context.Subject));
        Step("its surface is titled {string}", "The assistant's surface shows this title.", async (context, args) =>
        {
            var title = (await Surface(context).Read()).Definition.Title;
            if (title != args.Text(0)) { throw new StepFailedException($"The surface is titled \"{title}\"."); }
        });
        Step("its surface shows a chat and voice input", "The surface lays out a chat and a voice input.", async context =>
        {
            var layout = Assert((await Surface(context).Read()).Definition.Children, UIVocabulary.LayoutType);
            var children = (await context.Grains.GetGrain<ILayout>(layout.Name).Read()).Definition.Children;
            Assert(children, UIVocabulary.ChatType);
            Assert(children, UIVocabulary.VoiceInputType);
        });
        Step("I type {string}", "Types a message into the chat and submits it.", async (context, args) =>
        {
            var chat = Chat(context);
            await chat.SetDraft(args.Text(0));
            await chat.Submit();
        });
        Step("I say {string} by voice", "Records audio that the transcriber hears as this text.", (context, args) =>
            context.Grains.GetGrain<IVoiceInput>(UiComposer.NameOf(context.Subject, AssistantApp.VoicePart))
                .Capture(Encoding.UTF8.GetBytes(args.Text(0)), "audio/webm"));
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
        Step("the assistant used {string} and {string}", "The assistant's agent completed calls to both neuron methods.", async (context, args) =>
        {
            var completed = (await context.Grains.GetGrain<IAgent>(UiComposer.NameOf(context.Subject, "agent")).GetEventLog())
                .Where(entry => entry.Kind == "tool-completed").Select(entry => entry.Tool ?? "").ToArray();
            foreach (var method in new[] { args.Text(0), args.Text(1) })
            {
                if (!completed.Any(tool => tool.EndsWith("_" + method, StringComparison.Ordinal)))
                { throw new StepFailedException($"No completed call to {method} among [{string.Join(", ", completed)}]."); }
            }
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

    private static ISurface Surface(StepContext context) => context.Grains.GetGrain<ISurface>(UiComposer.NameOf(context.Subject, "surface"));

    private static IChat Chat(StepContext context) => context.Grains.GetGrain<IChat>(UiComposer.NameOf(context.Subject, AssistantApp.ChatPart));

    private static UiChildRef Assert(IReadOnlyList<UiChildRef> children, string kind) =>
        children.FirstOrDefault(child => child.Kind == kind)
        ?? throw new StepFailedException($"No {kind} among [{string.Join(", ", children.Select(child => child.Kind))}].");

    // The assistant answers one-way, so the chat is read until the message arrives.
    private static async Task Shows(StepContext context, ChatRole role, string text)
    {
        var deadline = DateTimeOffset.UtcNow + ChatTimeout;
        IReadOnlyList<ChatEntry> messages;
        while (!(messages = (await Chat(context).Read()).Messages).Any(message => message.Role == role && message.Text == text))
        {
            if (DateTimeOffset.UtcNow > deadline)
            { throw new StepFailedException($"The chat shows [{string.Join(" | ", messages.Select(message => $"{message.Role}: {message.Text}"))}]."); }
            await Task.Delay(TimeSpan.FromMilliseconds(50), context.CancellationToken);
        }
    }
}
