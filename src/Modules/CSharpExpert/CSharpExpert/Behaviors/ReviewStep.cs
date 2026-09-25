using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class ReviewStep(IDigitalBrain brain, string runId) : IBehavior, IBehaviorSignals
{
    public const string ApprovalReply = "APPROVE";

    public IReadOnlyList<Type> Signals => [typeof(TestsPassed)];

    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        await foreach (var _ in brain.On<TestsPassed>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                var snapshot = await run.Read().ConfigureAwait(false);
                var request = snapshot.Request ?? throw new InvalidOperationException("Review started before the feature request.");
                var profile = await brain.Get<ICodingProfile>(CodingWorkspace.Id(request.SolutionPath)).Read().ConfigureAwait(false);
                var diff = snapshot.Diff ?? string.Empty;
                var findings = DiffReview.Check(diff).ToList();
                if (findings.Count == 0 && profile.ReviewerAgentId is { Length: > 0 } reviewerId)
                {
                    findings.AddRange(await AskReviewerAsync(brain.Get<ICodingAgent>(reviewerId), profile.ReviewRules, diff, cancellation).ConfigureAwait(false));
                }

                await run.RecordReview(findings).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Reviewing the step failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<string>> AskReviewerAsync(ICodingAgent reviewer, string rules, string diff, CancellationToken cancellation)
    {
        var prompt = $"Review this C# diff against these rules: {rules}{Environment.NewLine}"
            + $"Reply {ApprovalReply} when it follows them, otherwise one finding per line.{Environment.NewLine}{diff}";
        var reply = await reviewer.Ask(prompt, cancellation).ConfigureAwait(false);
        return reply.Trim().Equals(ApprovalReply, StringComparison.OrdinalIgnoreCase)
            ? []
            : reply.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
