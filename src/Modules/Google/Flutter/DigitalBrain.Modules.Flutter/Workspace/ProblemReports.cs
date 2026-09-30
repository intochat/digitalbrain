using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Http;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Workspace;

// A "Report a problem" entry always carries the intent id it was raised from, so support can join
// it to the intent's usage, trace and statement line.
[GenerateSerializer, Alias("intochat.problem-report")]
public sealed record ProblemReport(
    [property: Id(0)] string IntentId,
    [property: Id(1)] string WorkspaceId,
    [property: Id(2)] string Message,
    [property: Id(3)] DateTimeOffset ReportedAt);

[GenerateSerializer, Alias("intochat.problem-reports-state")]
public sealed record ProblemReportsState
{
    [Id(0)] public List<ProblemReport> Reports { get; init; } = [];
}

[Alias("intochat.problem-reports"), Orleans.Metadata.DefaultGrainType("intochat.problem-reports")]
internal interface IProblemReports : INeuron
{
    Task<ProblemReport> Add(string workspaceId, string intentId, string message);
    Task<IReadOnlyList<ProblemReport>> Read();
}

[GrainType("intochat.problem-reports")]
internal sealed class ProblemReportsNeuron(
    [PersistentState("problem-reports", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ProblemReportsState> store)
    : Neuron, IProblemReports
{
    private const int MaxMessageLength = 4_000;

    public async Task<ProblemReport> Add(string workspaceId, string intentId, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > MaxMessageLength)
        {
            throw new ArgumentException($"A problem report is limited to {MaxMessageLength} characters.", nameof(message));
        }

        var report = new ProblemReport(intentId, workspaceId, message, DateTimeOffset.UtcNow);
        store.State.Reports.Add(report);
        try { await store.WriteStateAsync(); }
        catch { store.State.Reports.Remove(report); throw; }
        return report;
    }

    public Task<IReadOnlyList<ProblemReport>> Read() => Task.FromResult<IReadOnlyList<ProblemReport>>([.. store.State.Reports]);
}

internal sealed record ProblemReportInput(string? IntentId, string? Message);

internal static class ProblemReportEndpoint
{
    public static async Task<IResult> File(ProblemReportInput input, IDigitalBrain brain, CancellationToken ct)
    {
        if (!BrainScope.IsValidId(input.IntentId) || string.IsNullOrWhiteSpace(input.Message))
        { return Results.BadRequest(new { error = "A report needs the intent id it came from and a message." }); }
        try
        {
            var caller = CallerContextStamper.Require();
            var scope = BrainScope.Create(caller.AccountId, caller.BrainId);
            var report = await brain.Get<IProblemReports>(scope.Id).Add(scope.Name, input.IntentId!, input.Message!.Trim()).WaitAsync(ct);
            return Results.Created($"/brains/{scope.Name}/reports", report);
        }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    }
}
