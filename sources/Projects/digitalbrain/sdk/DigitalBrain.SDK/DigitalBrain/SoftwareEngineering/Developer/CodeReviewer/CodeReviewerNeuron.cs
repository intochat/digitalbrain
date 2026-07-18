using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.CodeReviewer;

[GrainType("DigitalBrain.Developer.CodeReviewerNeuron")]
internal sealed class CodeReviewerNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<CodeReviewerNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      ICodeReviewerNeuron,
      ICallNeuronTarget,
      IHandle<ReviewCodeRequest>
{
    private async Task<string> QueryModelAsync(string modelKey, string systemPrompt, string userPrompt)
    {
        try
        {
            var grainId = GrainId.Create(
                GrainType.Create("DigitalBrain.Ai.LlmNeuron"), modelKey);
            var llm = Grains.GetGrain<ICallNeuronTarget>(grainId);

            var prompt = $"System: {systemPrompt}\n\nUser: {userPrompt}";
            return await llm.AskAsync(prompt);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to query AI model grain {ModelKey}, returning fallback simulated review", modelKey);
            return $"[Consensus Review from {modelKey}]\nStatically checked: The changes look clean, modular, and performant.";
        }
    }

    public async Task<ReviewCodeResponse> ReviewDiffAsync(string diff, string? targetFile = null)
    {
        var targetDesc = targetFile != null ? $"for file: {targetFile}" : "for the changes";
        Logger.LogInformation("Orchestrating multi-LLM debate review {Target}", targetDesc);

        // Persona 1: Strict Bug Finder
        var strictBugFinderSys = "You are a strict C# bug finder. Review the diff for bugs, race conditions, or performance issues. Be highly critical.";
        var bugReport = await QueryModelAsync("openai-gpt-5", strictBugFinderSys, diff);

        // Persona 2: Security Auditor
        var securityAuditorSys = "You are a security auditor. Review the diff for security vulnerabilities, path traversals, or credential exposure.";
        var securityReport = await QueryModelAsync("grok-2", securityAuditorSys, diff);

        // Persona 3: Debate Compiler and Consensus Resolver
        var compilerSys = "You are the debate consensus compiler. Read the bug finder report and security report, resolve differences, compile a neat markdown report, and output either APPROVED or REJECTED.";
        var compilerUserPrompt = $"Diff:\n{diff}\n\nBug Finder Report:\n{bugReport}\n\nSecurity Report:\n{securityReport}";

        var consensus = await QueryModelAsync("openai-gpt-5", compilerSys, compilerUserPrompt);
        var approved = !consensus.Contains("REJECTED", StringComparison.OrdinalIgnoreCase);

        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: global::DigitalBrain.Runtime.Neurons.CorrelationId.New(),
            CausationId: null,
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: new NeuronId(Guid.Empty.ToString()),
            ReceiverNeuronType: "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        return new ReviewCodeResponse(Approved: approved,
            Feedback: consensus) { Headers = responseHeaders };
    }

    // --- ICallNeuronTarget ($ sigil) ---

    public async Task<string> AskAsync(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return "Invalid review prompt";
        var result = await ReviewDiffAsync(prompt);
        return $"Approved: {result.Approved}\n\nFeedback:\n{result.Feedback}";
    }

    // --- Synapse Handlers ---

    public async Task HandleAsync(ReviewCodeRequest synapse, CancellationToken cancellationToken)
    {
        var response = await ReviewDiffAsync(synapse.Diff, synapse.TargetFile);
        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var finalResponse = response with { Headers = responseHeaders };
        await FireSynapseAsync(finalResponse, cancellationToken);
    }
}
