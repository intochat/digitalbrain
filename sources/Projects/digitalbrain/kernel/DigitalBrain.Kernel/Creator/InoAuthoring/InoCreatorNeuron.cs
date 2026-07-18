using System.Globalization;
using DigitalBrain.Runtime.Dynamic;
using DigitalBrain.Kernel.Runtime;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Tasks;
using System.Text.Json.Nodes;

namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 close-out. The synapse-on-grain rung that completes the
// "Creator retargets to InoLang author" deliverable started by sub-issues
// B + C (the in-process InoAuthoringLoop service + the system prompt /
// generated-source / persistence substrate).
//
// Flow:
//   1. AuthorInoNeuronRequest arrives via the stream subscription (gateway
//      RouteAsync → receiver-type stream → OnNextAsync → HandleSynapseAsync).
//   2. The grain awaits InoAuthoringLoop.AuthorAsync under a per-handler
//      timeout. The loop runs prompt → .ino → InoCompiler diagnostics →
//      ScenarioRunner red→green → persist (gate-before-persist).
//   3. On green, the grain hot-registers the interpreted neuron immediately;
//      it does not wait for restart discovery of the persisted `.ino`.
//   4. On outcome (green OR give-up), the grain broadcasts
//      `DigitalBrain.Creator.InoNeuronAuthored` via SynapseBroadcaster's
//      port-less BroadcastSystemSignalAsync overload.
[ImplicitStreamSubscription(InoCreatorNeuronType)]
public sealed class InoCreatorNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    InoAuthoringLoop authoringLoop,
    IInterpretedNeuronRegistry interpretedRegistry,
    SynapseBroadcaster signalBroadcaster,
    ILogger<InoCreatorNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IInoCreatorNeuron,
      INeuronMetadata,
      IHandle<AuthorInoNeuronRequest>
{
    public const string InoCreatorNeuronType = nameof(InoCreatorNeuron);
    public const string AuthoredSignalFqn = "DigitalBrain.Creator.InoNeuronAuthored";

    public const string StatusPromoted = "promoted";
    public const string StatusFailed = "failed";

    public static NeuronId         Id           => new("kernel/ino-creator");
    public static string           Icon         => "creator";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    // Single source of truth for the outcome signal's payload-field names.
    public static readonly string[] AuthoredSignalFields =
    {
        "fqn",
        "relativePath",
        "attempts",
        "status",
    };

    // Per-handler ceiling.
    static readonly TimeSpan AuthoringHandlerTimeout = TimeSpan.FromMinutes(5);

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // Resume any pending/unfinished authoring tasks in the background!
        _ = Task.Run(() => ResumePendingAuthoringTasksAsync(cancellationToken), cancellationToken);
    }

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is not AuthorInoNeuronRequest req)
        {
            logger.LogWarning(
                "InoCreatorNeuron received unexpected synapse type {SynapseType}; expected {Expected}.",
                synapse.GetType().FullName, typeof(AuthorInoNeuronRequest).FullName);
            return;
        }

        // Delegate the authoring run to the background so that the gateway/stream is not blocked,
        // since the caller/client awaits the task durably via the DTCS grain.
        _ = Task.Run(() => RunAuthoringTaskAsync(req));
    }

    private async Task ResumePendingAuthoringTasksAsync(CancellationToken ct)
    {
        // Give the silo a moment to boot
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        var pendingRequests = new List<AuthorInoNeuronRequest>();
        
        lock (Incoming)
        {
            foreach (var synapse in Incoming)
            {
                if (synapse is AuthorInoNeuronRequest req)
                {
                    var correlationId = req.Headers?.CorrelationId.Value;
                    if (correlationId == null) continue;
                    
                    // Check if we already have a finished outcome/progress in Outgoing
                    bool finished = false;
                    lock (Outgoing)
                    {
                        foreach (var outSynapse in Outgoing)
                        {
                            if (outSynapse.Headers?.CorrelationId.Value == correlationId)
                            {
                                // We stamp all outgoing progress/authoring synapses with the same correlation ID.
                                // If we find an InoAuthoringProgress that is in "Activating" step or completed, we consider it finished,
                                // or if there's any other indicator.
                                if (outSynapse is InoAuthoringProgress progress && progress.Step == "Activating")
                                {
                                    finished = true;
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (!finished)
                    {
                        pendingRequests.Add(req);
                    }
                }
            }
        }
        
        foreach (var req in pendingRequests)
        {
            logger.LogInformation("Resuming pending/unfinished Creator authoring task for intent: '{Intent}'", req.Intent);
            _ = Task.Run(() => RunAuthoringTaskAsync(req), ct);
        }
    }

    private async Task RunAuthoringTaskAsync(AuthorInoNeuronRequest req)
    {
        var correlationId = req.Headers?.CorrelationId.Value.ToString() ?? Guid.NewGuid().ToString();
        var completion = Grains.GetGrain<IDurableTaskCompletionSourceGrain>(correlationId);
        
        // Suppress if already completed (survived a crash/restart after writing state but before returning)
        var stateResult = await completion.GetState();
        if (stateResult.IsCompleted)
        {
            logger.LogInformation("Authoring task {CorrelationId} is already completed.", correlationId);
            return;
        }

        var loopRequest = new InoAuthoringRequest(
            Intent: req.Intent,
            SuggestedFqn: req.SuggestedFqn,
            LlmModelKey: req.LlmModelKey,
            MaxAttempts: req.MaxAttempts > 0 ? req.MaxAttempts : 5);

        using var cts = new CancellationTokenSource(AuthoringHandlerTimeout);

        InoAuthoringResult result;
        try
        {
            int currentAttempt = 1;
            result = await authoringLoop.AuthorAsync(loopRequest, async (step, draft, errors) =>
            {
                if (step == "Prompting" && errors is not null)
                {
                    currentAttempt++;
                }
                
                // Copy headers to maintain proper correlation
                var progressSynapse = new InoAuthoringProgress(
                    Step: step,
                    SuggestedFqn: req.SuggestedFqn,
                    Attempt: currentAttempt,
                    InoSource: draft,
                    DiagnosticErrors: errors
                )
                {
                    Headers = SynapseMetadata.Create(
                        synapseId: Guid.NewGuid(),
                        correlationId: req.Headers?.CorrelationId.Value ?? Guid.Empty,
                        causationId: req.Headers?.SynapseId.Value ?? Guid.Empty,
                        callerNeuronId: InstanceId,
                        callerNeuronType: NeuronType,
                        receiverNeuronId: Guid.Empty,
                        receiverNeuronType: "HomeFeed",
                        timestamp: DateTimeOffset.UtcNow
                    )
                };

                await FireSynapseAsync(progressSynapse, cts.Token);
                await RenderProgressUiAsync(step, currentAttempt, draft, errors, cts.Token);
            }, cts.Token);

            if (result.Green)
            {
                var registration = result.Registration
                    ?? throw new InvalidOperationException(
                        "A green .ino authoring result did not include a runtime registration.");
                await interpretedRegistry.RegisterDynamicAsync(registration);
                
                // Complete the DTCS!
                await completion.TrySetResult(result.RelativeInoPath ?? string.Empty);
            }
            else
            {
                await completion.TrySetException(result.FinalError ?? "Authoring loop failed to produce green result.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "InoAuthoringLoop threw for intent '{Intent}' (suggested {Fqn}).",
                req.Intent, req.SuggestedFqn);
                
            await completion.TrySetException(ex.Message);
            
            await BroadcastOutcomeAsync(
                fqn: req.SuggestedFqn,
                relativePath: string.Empty,
                attempts: 0,
                status: StatusFailed,
                cts.Token);
            return;
        }

        await BroadcastOutcomeAsync(
            fqn: result.AuthoredFqn ?? req.SuggestedFqn,
            relativePath: result.RelativeInoPath ?? string.Empty,
            attempts: result.Attempts,
            status: result.Green ? StatusPromoted : StatusFailed,
            cts.Token);
    }

    private Task RenderProgressUiAsync(string step, int attempt, string? source, string? errors, CancellationToken ct)
    {
        var data = new JsonObject
        {
            ["title"] = "Ino Forge",
            ["subtitle"] = "Self-Authoring Neuron Loop",
            ["intent"] = "Authoring: " + step,
            ["step"] = step,
            ["attempt"] = attempt.ToString(CultureInfo.InvariantCulture),
            ["source"] = source ?? string.Empty,
            ["errors"] = errors ?? string.Empty,
            ["initials"] = "IF",
            ["tone"] = "purple"
        };
        
        return RenderAsync("digitalbrain", "sample_neuron", data, ct);
    }

    Task BroadcastOutcomeAsync(string fqn, string relativePath, int attempts, string status, CancellationToken ct)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthoredSignalFields[0]] = fqn,
            [AuthoredSignalFields[1]] = relativePath,
            [AuthoredSignalFields[2]] = attempts.ToString(CultureInfo.InvariantCulture),
            [AuthoredSignalFields[3]] = status,
        };
        return signalBroadcaster.BroadcastSystemSignalAsync(AuthoredSignalFqn, payload, ct);
    }
}

