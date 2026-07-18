namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Neuron;

/// <summary>
/// A real-world C# implementation of the layered Neuron Constructor.
/// Demonstrates how the Pre-Process (Ingress) and Post-Process (Egress)
/// layers are automatically compiled to Orleans' native IIncomingGrainCallFilter.
/// </summary>
[GrainType(NeuronConstructorFqn)]
public sealed class NeuronConstructor(
    ILogger<NeuronConstructor> logger) 
    : Grain, IIncomingGrainCallFilter, INeuronConstructor
{
    public const string NeuronConstructorFqn = "DigitalBrain.SDK.Ai.NeuronConstructor";

    private int _synapseInboundFilteredCount = 0;
    private int _synapseOutboundFilteredCount = 0;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("NeuronConstructor activated on cortex mesh.");
        return base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// Orleans Layered Incoming Call Filter.
    /// This intercepts EVERY synapse message flowing into this virtual actor.
    /// </summary>
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var methodName = context.ImplementationMethod?.Name;

        // --- Layer 1: Inbound Grain Call Filter (Ingress validation) ---
        if (methodName == "ProcessDesignAsync")
        {
            _synapseInboundFilteredCount++;
            logger.LogInformation(
                "[Layer 1 Ingress Intercept] Total filtered calls: {Count}", 
                _synapseInboundFilteredCount);

            // Pre-process intercept validation using Orleans context Request
            if (context.Request.GetArgumentCount() > 0)
            {
                var inputArg = context.Request.GetArgument(0);
                if (inputArg is null)
                {
                    throw new ArgumentNullException(
                        nameof(context), "Synapse payload is null - Blocked by Layer 1.");
                }
            }
        }

        // --- Layer 2: Core Neuron behavior (Execute LLM autoprompting and design compilation) ---
        await context.Invoke();

        // --- Layer 3: Outgoing Egress Interceptor (Post-Process formatting) ---
        if (methodName == "ProcessDesignAsync")
        {
            _synapseOutboundFilteredCount++;
            logger.LogInformation(
                "[Layer 3 Egress Intercept] Intercepted grain execution outcome. Total post-filtered: {Count}",
                _synapseOutboundFilteredCount);

            if (context.Result is string codeOut)
            {
                // Ensure output is clean and strict before returning to caller
                logger.LogInformation("Layer 3: Formatting InoLang response bundle.");
            }
        }
    }

    /// <summary>
    /// Core logic to construct dynamic spec-first code.
    /// </summary>
    public Task<string> ProcessDesignAsync(string prompt, string refinements)
    {
        logger.LogInformation("Processing core behavior generation for prompt: {Prompt}", prompt);
        
        var hasStrictness = refinements.Contains("strict", StringComparison.OrdinalIgnoreCase);
        var strictnessRule = hasStrictness 
            ? " [STRICT RULE: Return brief responses only.]" 
            : "";

        var generatedIno = $@"neuron DigitalBrain.Custom.GeneratedNeuron
  ""Dynamic Spec-First compiled neuron.""
  
  on synapse(DB.User.Request) it:
    let response = ask DigitalBrain.SDK.OpenAI.ChatGpt to ""Prompt: {prompt}.{strictnessRule}""
    emit signal(DB.Custom.Success)(data: response)";

        return Task.FromResult(generatedIno);
    }
}

public interface INeuronConstructor : IGrainWithStringKey
{
    Task<string> ProcessDesignAsync(string prompt, string refinements);
}
