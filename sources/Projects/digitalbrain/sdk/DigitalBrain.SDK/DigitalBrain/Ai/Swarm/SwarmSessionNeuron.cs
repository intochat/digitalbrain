using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Swarm;

public interface ISwarmSessionNeuron : INeuron;

[ImplicitStreamSubscription(nameof(SwarmSessionNeuron))]
internal sealed class SwarmSessionNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<SwarmSessionNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      ISwarmSessionNeuron,
      INeuronMetadata,
      IHandle<RequestSwarmAnalysis>,
      IHandle<SwarmFindingProposed>
{
    public static NeuronId Id => new("swarm/session-coordinator");
    public static string Icon => "swarm";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    private readonly Dictionary<Guid, SwarmActiveSession> _sessions = new();

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is RequestSwarmAnalysis or SwarmFindingProposed)
        {
            await DispatchSynapseAsync(synapse);
        }
    }

    public async Task HandleAsync(RequestSwarmAnalysis request, CancellationToken ct)
    {
        Logger.LogInformation("SwarmSessionNeuron received analysis request for path: {Path}", request.ProjectPath);

        var sessionId = Guid.NewGuid();
        var workspace = new SwarmWorkspace();

        // Load files in-memory
        var filesToLoad = new Dictionary<string, string>();
        if (Directory.Exists(request.ProjectPath))
        {
            try
            {
                var files = Directory.GetFiles(request.ProjectPath, "*.cs", SearchOption.AllDirectories)
                    .Take(10); // Limit to top 10 files to keep execution lightweight
                foreach (var file in files)
                {
                    filesToLoad[Path.GetFileName(file)] = await File.ReadAllTextAsync(file, ct);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to read files from path {Path}", request.ProjectPath);
            }
        }

        // Fallback or default sample files for testing/empty environments
        if (filesToLoad.Count == 0)
        {
            filesToLoad["SampleService.cs"] = 
                "using System;\n" +
                "namespace Test;\n" +
                "public class SampleService {\n" +
                "    public void Execute() {\n" +
                "        Console.WriteLine(\"Executing...\");\n" +
                "    }\n" +
                "}";
            
            filesToLoad["IDataRepository.cs"] =
                "namespace Test;\n" +
                "public interface IDataRepository {\n" +
                "    string GetData();\n" +
                "}";
        }

        foreach (var file in filesToLoad)
        {
            workspace.AddOrUpdateDocument(file.Key, file.Value);
        }

        var docNames = workspace.DocumentNames.ToList();
        var session = new SwarmActiveSession(sessionId, workspace, docNames.Count, request.CorrelationId, request.SynapseId);
        _sessions[sessionId] = session;

        // Subscribe session coordinator to session stream
        var streamProvider = this.GetStreamProvider("synapse-streams");
        var stream = streamProvider.GetStream<Synapse>(StreamId.Create("swarm-session", sessionId));
        await stream.SubscribeAsync(this);

        Logger.LogInformation("Spawning {Count} SwarmAgentNeurons to review {Files} documents in parallel", 
            request.WorkerCount, docNames.Count);

        // Dispatch each document to a separate worker grain
        for (int i = 0; i < docNames.Count; i++)
        {
            var docName = docNames[i];
            var source = filesToLoad[docName];
            var workerId = Guid.NewGuid();
            var worker = Grains.GetGrain<ISwarmAgentNeuron>(workerId);

            // Register session stream on worker
            await worker.RegisterSessionAsync(sessionId);

            var assignment = new SwarmDocumentAssigned(SessionId: sessionId,
        DocumentName: docName,
        SourceCode: source) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(SwarmSessionNeuron),
            receiverNeuronId: workerId,
            receiverNeuronType: nameof(SwarmAgentNeuron),
            timestamp: DateTimeOffset.UtcNow
        ) };

            // Send assignment synapse via session stream
            await stream.OnNextAsync(assignment);
        }
    }

    public async Task HandleAsync(SwarmFindingProposed proposed, CancellationToken ct)
    {
        Logger.LogInformation("SwarmSessionNeuron received finding from worker for file: {File}", proposed.DocumentName);

        if (_sessions.TryGetValue(proposed.SessionId, out var session))
        {
            session.Findings.Add(proposed);
            session.ReviewedFiles.Add(proposed.DocumentName);

            // Check if all files have been reviewed
            if (session.ReviewedFiles.Count >= session.TotalFilesExpected)
            {
                Logger.LogInformation("All {Count} files reviewed. Swarm completed. Generating final report...", session.TotalFilesExpected);

                var report = $"Swarm Analysis Session Completed successfully.\n" +
                             $"- Total files reviewed: {session.TotalFilesExpected}\n" +
                             $"- Total findings proposed: {session.Findings.Count}\n" +
                             $"- Findings:\n" +
                             string.Join("\n", session.Findings.Select(f => $"  * [{f.Severity.ToUpper()}] {f.DocumentName}:L{f.LineNumber} -> {f.FindingMessage}"));

                var completed = new SwarmSessionCompleted(SessionId: proposed.SessionId,
        TotalFilesReviewed: session.TotalFilesExpected,
        TotalFindingsFound: session.Findings.Count,
        SummaryReport: report) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: session.CorrelationId,
            causationId: session.CausationId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(SwarmSessionNeuron),
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "External",
            timestamp: DateTimeOffset.UtcNow
        ) };

                // Publish completed report synapse back to main synapse stream
                var prevContext = NeuronContext.Value;
                try
                {
                    NeuronContext.Value = null;
                    await FireSynapseAsync(completed);
                }
                finally
                {
                    NeuronContext.Value = prevContext;
                }
                _sessions.Remove(proposed.SessionId);
            }
        }
    }
}

internal sealed class SwarmActiveSession(Guid sessionId, SwarmWorkspace workspace, int totalFilesExpected, Guid correlationId, Guid causationId)
{
    public Guid SessionId { get; } = sessionId;
    public SwarmWorkspace Workspace { get; } = workspace;
    public int TotalFilesExpected { get; } = totalFilesExpected;
    public Guid CorrelationId { get; } = correlationId;
    public Guid CausationId { get; } = causationId;
    public HashSet<string> ReviewedFiles { get; } = new(StringComparer.Ordinal);
    public List<SwarmFindingProposed> Findings { get; } = new();
}
