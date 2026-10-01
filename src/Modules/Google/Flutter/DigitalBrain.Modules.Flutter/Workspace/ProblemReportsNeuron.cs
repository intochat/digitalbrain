using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Workspace;

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
