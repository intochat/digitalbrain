using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Workspace;

[Alias("intochat.problem-reports"), Orleans.Metadata.DefaultGrainType("intochat.problem-reports")]
internal interface IProblemReports : INeuron
{
    Task<ProblemReport> Add(string workspaceId, string intentId, string message);
    Task<IReadOnlyList<ProblemReport>> Read();
}
