using DigitalBrain.Specs;

namespace DigitalBrain.Apps;

// Steps every app spec can use. The subject of a run is the key of the app under test.
public sealed class AppSteps : StepLibrary
{
    public const string AnswerKey = "answer";
    public const string InvocationKey = "invocation";
    public const string ErrorKey = "error";
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public override string Name => "App";

    public AppSteps()
    {
        Step("the setting {string} is {string}", "Configures one of the app's settings before it is used.", async (context, args) =>
        {
            var app = context.Grains.GetGrain<IApp>(context.Subject);
            await app.Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string> { [args.Text(0)] = args.Text(1) }));
        });
        Step("I ask {string}", "Sends a question to the app and waits for its answer.", (context, args) => Ask(context, "ask", args.Text(0)));
        Step("I ask:", "Sends the doc string below to the app and waits for its answer.", (context, args) => Ask(context, "ask", args.DocString));
        Step("I use {string} with {string}", "Invokes a named operation of the app and waits for its answer.", (context, args) => Ask(context, args.Text(0), args.Text(1)));
        Step("the answer is {string}", "The app answered exactly this text.", (context, args) =>
        {
            var answer = Answer(context);
            return answer == args.Text(0) ? Task.CompletedTask : throw new StepFailedException($"The answer was \"{Shorten(answer)}\".");
        });
        Step("the answer mentions {string}", "The app's answer contains this text, ignoring case.", (context, args) =>
        {
            var answer = Answer(context);
            return answer.Contains(args.Text(0), StringComparison.OrdinalIgnoreCase)
                ? Task.CompletedTask
                : throw new StepFailedException($"The answer does not mention \"{args.Text(0)}\": \"{Shorten(answer)}\".");
        });
        Step("the app fails with {string}", "The app refused the last request with an error mentioning this text.", (context, args) =>
        {
            var error = context.Recall<string>(ErrorKey);
            return error.Contains(args.Text(0), StringComparison.OrdinalIgnoreCase)
                ? Task.CompletedTask
                : throw new StepFailedException($"The app failed with \"{Shorten(error)}\".");
        });
    }

    private static async Task Ask(StepContext context, string operation, string input)
    {
        var app = context.Grains.GetGrain<IApp>(context.Subject);
        var invocation = await app.Invoke(new InvokeApp(Guid.NewGuid(), operation, input));
        context.Remember(InvocationKey, invocation.Id);
        var deadline = DateTimeOffset.UtcNow + AnswerTimeout;
        while (invocation.Status == InvocationStatus.Pending)
        {
            if (DateTimeOffset.UtcNow > deadline) { throw new StepFailedException($"The app did not answer within {AnswerTimeout.TotalMinutes} minutes."); }
            await Task.Delay(PollInterval, context.CancellationToken);
            invocation = await app.ReadInvocation(invocation.Id);
        }
        if (invocation.Status == InvocationStatus.Failed) { context.Remember(ErrorKey, invocation.Error ?? "no reason given"); }
        context.Remember(AnswerKey, invocation.Output ?? "");
    }

    // An answer step after a failed request reports why the app failed instead of an empty answer.
    public static string Answer(StepContext context) => context.TryRecall<string>(ErrorKey, out var error)
        ? throw new StepFailedException($"The app failed: {error}")
        : context.Recall<string>(AnswerKey);

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
