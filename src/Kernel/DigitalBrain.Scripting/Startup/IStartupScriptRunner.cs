using DigitalBrain.Abstractions;

namespace DigitalBrain.Scripting.Startup;

internal sealed record StartupScriptRunResult(
    bool IsSuccess,
    string Summary,
    IReadOnlyList<string> Diagnostics);

internal interface IStartupScriptRunner
{
    Task<StartupScriptRunResult> RunAsync(
        StartupScript script,
        IDigitalBrain brain,
        CancellationToken cancellationToken);
}
