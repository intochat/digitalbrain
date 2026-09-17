using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Programming;
using DigitalBrain.Core.Programming;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace IntoChat;

[McpServerToolType]
public sealed class ProgramTools(ProgramService programs, ProgramCompiler compiler)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "program_list"), Description("List deployed living neuron programs and their current versions, enabled state, definitions and recent run IDs.")]
    public Task<string> List(CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(await programs.ListAsync(cancellationToken), Json));

    [McpServerTool(Name = "program_read"), Description("Read the current program, retained versions and run IDs before editing, pausing or rolling back. The neuron address is program:<id>.")]
    public Task<string> Read(string id, CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(await programs.ReadAsync(id, cancellationToken), Json));

    [McpServerTool(Name = "program_examples"), Description("Get runnable examples and the exact neuron/synapse programming schema. Templates reference {{input.path}}, {{value.path}} and {{nodes.id.path}}.")]
    public string Examples() => Guard(() => JsonSerializer.Serialize(ProgramExamples.All, Json));

    [McpServerTool(Name = "program_compile"), Description("Compile a natural-language behavior to a validated neuron graph draft. This does not deploy; show the draft for user review before program_deploy.")]
    public Task<string> Compile(string intent, CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(await compiler.CompileAsync(intent, cancellationToken), Json));

    [McpServerTool(Name = "program_validate"), Description("Validate a JSON program definition: node configuration, typed ports, edges, cycles and execution order. No changes are made.")]
    public string Validate(string definition)
        => Guard(() => JsonSerializer.Serialize(programs.Validate(Parse(definition)), Json));

    [McpServerTool(Name = "program_deploy"), Description("Deploy an explicitly approved JSON neuron graph immediately without restarting. Supply expectedVersion from program_read to protect edits. New runs use this version; active runs keep their original graph. Code nodes run trusted local C# with OS access: deploy only source explicitly approved by the user.")]
    public Task<string> Deploy(string definition, long? expectedVersion = null, CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(await programs.DeployAsync(Parse(definition), expectedVersion, cancellationToken), Json));

    [McpServerTool(Name = "program_run"), Description("Run a deployed program with JSON input. An optional stable runId deduplicates retries. Returns durable status and steps; use program_run_read to observe completion.")]
    public Task<string> Run(string id, string input, string? runId = null, CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            using var body = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(await programs.RunAsync(id, body.RootElement.Clone(), runId, cancellationToken), Json);
        });

    [McpServerTool(Name = "program_run_read"), Description("Read a durable run's pinned graph version, input, output and per-neuron checkpoints, including errors. Status is Running, Completed, Filtered, Failed or Cancelled.")]
    public Task<string> ReadRun(string id, string runId, CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(await programs.ReadRunAsync(id, runId, cancellationToken), Json));

    [McpServerTool(Name = "program_enabled"), Description("Pause or resume a deployed program. Paused programs reject new runs and ignore trigger signals; previously started runs retain their version.")]
    public Task<string> Enabled(string id, bool enabled, long? expectedVersion = null, CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(await programs.SetEnabledAsync(id, enabled, expectedVersion, cancellationToken), Json));

    [McpServerTool(Name = "program_rollback"), Description("Deploy one of the retained historical definitions as a new current revision. Read the program first and pass expectedVersion.")]
    public Task<string> Rollback(string id, long version, long? expectedVersion = null, CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(await programs.RollbackAsync(id, version, expectedVersion, cancellationToken), Json));

    [McpServerTool(Name = "program_cancel"), Description("Cancel a run so no further steps are dispatched. An already running external operation may finish.")]
    public Task<string> Cancel(string id, string runId, CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            await programs.CancelAsync(id, runId, cancellationToken);
            return "Cancellation requested.";
        });

    public IReadOnlyList<AITool> AgentTools() =>
    [
        AIFunctionFactory.Create(List, "program_list"),
        AIFunctionFactory.Create(Read, "program_read"),
        AIFunctionFactory.Create(Examples, "program_examples"),
        AIFunctionFactory.Create(Compile, "program_compile"),
        AIFunctionFactory.Create(Validate, "program_validate"),
        AIFunctionFactory.Create(Deploy, "program_deploy"),
        AIFunctionFactory.Create(Run, "program_run"),
        AIFunctionFactory.Create(ReadRun, "program_run_read"),
        AIFunctionFactory.Create(Enabled, "program_enabled"),
        AIFunctionFactory.Create(Rollback, "program_rollback"),
        AIFunctionFactory.Create(Cancel, "program_cancel"),
    ];

    private static ProgramDefinition Parse(string definition) => JsonSerializer.Deserialize<ProgramDefinition>(definition, Json)
        ?? throw new ArgumentException("Provide a JSON program definition.");

    private static string Guard(Func<string> operation)
    {
        try { return operation(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { throw new ModelContextProtocol.McpException(error.Message, error); }
    }

    private static async Task<string> Guard(Func<Task<string>> operation)
    {
        try { return await operation(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { throw new ModelContextProtocol.McpException(error.Message, error); }
    }
}
