using DigitalBrain.AI;
using DigitalBrain.AI.GroupChat;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Apps;
using DigitalBrain.Specs;

namespace IntoChat.Marketplace;

// Steps about models: scripted stand-ins that make a scenario deterministic, and a judge for answers
// that only a model can assess.
internal sealed class ModelSteps : StepLibrary
{
    private const int JudgeVotes = 3;
    private const string ReplySeparator = "---";

    public override string Name => "Models";

    public ModelSteps(IConfiguration configuration)
    {
        var judgeModel = configuration["IntoChat:Specs:JudgeModel"] ?? nameof(IGpt56Luna);
        Step("the scripted model {string} replies:", "A stand-in model answers with the doc string's replies in order, separated by lines of ---.", (context, args) =>
        {
            var replies = args.DocString.Split('\n').Aggregate(new List<List<string>> { new() }, (groups, line) =>
            {
                if (line.Trim() == ReplySeparator) { groups.Add([]); } else { groups[^1].Add(line); }
                return groups;
            }).Select(lines => string.Join("\n", lines).Trim()).ToArray();
            return context.Grains.GetGrain<IScriptedLLM>(ScriptedKey(context, args.Text(0))).Script(replies);
        });
        Step("the setting {string} is the scripted model {string}", "Points a model setting of the app at a scripted stand-in.", async (context, args) =>
        {
            var model = IScriptedLLM.ModelPrefix + ScriptedKey(context, args.Text(1));
            await context.Grains.GetGrain<IApp>(context.Subject).Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string> { [args.Text(0)] = model }));
        });
        Step("the scripted model {string} was told {string}", "Some prompt the scripted model received contains this text.", async (context, args) =>
        {
            var prompts = await context.Grains.GetGrain<IScriptedLLM>(ScriptedKey(context, args.Text(0))).Prompts();
            if (!prompts.Any(prompt => prompt.Contains(args.Text(1), StringComparison.OrdinalIgnoreCase)))
            { throw new StepFailedException($"The scripted model \"{args.Text(0)}\" was never told \"{args.Text(1)}\"."); }
        });
        Step("the answer satisfies {string}", $"A judge model decides; at least {JudgeVotes / 2 + 1} of {JudgeVotes} verdicts must agree.", async (context, args) =>
        {
            var answer = AppSteps.Answer(context);
            var approvals = 0;
            var reasons = new List<string>();
            for (var vote = 0; vote < JudgeVotes; vote++)
            {
                var verdict = await ModelAddress.Complete(context.Grains, judgeModel,
                    "You judge whether an answer meets a criterion. Reply with YES or NO on the first line, then one short reason.",
                    $"Criterion: {args.Text(0)}\n\nAnswer:\n{answer}", context.CancellationToken);
                if (verdict.StartsWith("YES", StringComparison.OrdinalIgnoreCase)) { approvals++; } else { reasons.Add(verdict); }
            }
            if (approvals <= JudgeVotes / 2)
            { throw new StepFailedException($"The judge approved {approvals} of {JudgeVotes}: {reasons.FirstOrDefault()}"); }
        });
    }

    // Scoped to the app under test, so parallel or repeated runs never share a script.
    private static string ScriptedKey(StepContext context, string name) => context.Subject + "/" + name;
}

// Steps about the discussion a group chat app held for the last question.
internal sealed class GroupChatSteps : StepLibrary
{
    public override string Name => "Group chat";

    public GroupChatSteps()
    {
        Step("{string} and {string} take turns, starting with {string}", "Speakers alternate, and the named one speaks first.", async (context, args) =>
        {
            var speakers = (await Discussion(context)).Turns.Select(turn => turn.Speaker).ToArray();
            var first = args.Text(2);
            var second = first == args.Text(0) ? args.Text(1) : args.Text(0);
            var expected = speakers.Select((_, index) => index % 2 == 0 ? first : second);
            if (speakers.Length < 2 || !speakers.SequenceEqual(expected))
            { throw new StepFailedException($"The turns went {string.Join(", ", speakers)}."); }
        });
        Step("the discussion ends after {int} rounds", "The discussion took exactly this many rounds.", async (context, args) =>
        {
            var rounds = Rounds(await Discussion(context));
            if (rounds != args.Int(0)) { throw new StepFailedException($"The discussion took {rounds} rounds."); }
        });
        Step("the discussion ends within {int} rounds", "The discussion took at most this many rounds.", async (context, args) =>
        {
            var rounds = Rounds(await Discussion(context));
            if (rounds > args.Int(0)) { throw new StepFailedException($"The discussion took {rounds} rounds."); }
        });
        Step("the participants agree", "Every participant agreed in the last round.", async (context, _) =>
        {
            if (!(await Discussion(context)).Agreed) { throw new StepFailedException("The participants did not agree."); }
        });
        Step("the participants do not agree", "The discussion hit the round limit without agreement.", async (context, _) =>
        {
            if ((await Discussion(context)).Agreed) { throw new StepFailedException("The participants agreed."); }
        });
    }

    private static int Rounds(GroupChatState discussion) => discussion.Turns.Count == 0 ? 0 : discussion.Turns.Max(turn => turn.Round);

    private static async Task<GroupChatState> Discussion(StepContext context)
    {
        var chat = context.Grains.GetGrain<IGroupChat>(GroupChatRuntime.ChatKey(context.Subject, context.Recall<Guid>(AppSteps.InvocationKey)));
        var discussion = await chat.Read();
        return discussion.Status switch
        {
            GroupChatStatus.Idle => throw new StepFailedException(context.TryRecall<string>(AppSteps.ErrorKey, out var error)
                ? $"No discussion took place because the app failed: {error}"
                : "No discussion took place."),
            GroupChatStatus.Failed => throw new StepFailedException($"The discussion failed after {discussion.Turns.Count} turns: {discussion.Failure}"),
            _ => discussion,
        };
    }
}
