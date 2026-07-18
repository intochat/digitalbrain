using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.Intent;
using DigitalBrain.SDK.DigitalBrain.Ai.Planning;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm;

public sealed class MockChatClientAutoPrimer(
    IServiceProvider services,
    MockModelRegistry registry,
    ILogger<MockChatClientAutoPrimer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var (examples, intentExamples, plannerExamples, featureCount) = LoadAllExamples();
        var primedExampleCount = 0;
        var primedModelCount = 0;

        foreach (var modelId in registry.LlmModelIds)
        {
            var client = services.GetKeyedService<IChatClient>(modelId);
            var mock = client?.GetService(typeof(BddMockChatClient)) as BddMockChatClient 
                       ?? client as BddMockChatClient;
            if (mock is null) continue;
            primedModelCount++;
            foreach (var (prompt, response) in examples)
            {
                mock.Prime(BddMockChatClient.FingerprintForUserPrompt(prompt), response);
                primedExampleCount++;
            }
            foreach (var (transcript, response) in intentExamples)
            {
                mock.Prime(
                    BddMockChatClient.FingerprintForSystemAndUserPrompt(IntentNeuron.SystemPrompt, transcript),
                    response);
                primedExampleCount++;
            }
            foreach (var (intent, response) in plannerExamples)
            {
                mock.Prime(
                    BddMockChatClient.FingerprintForSystemAndUserPrompt(PlannerNeuron.SystemPrompt, intent),
                    response);
                primedExampleCount++;
            }
        }

        logger.LogInformation(
            "Auto-primed {ExampleCount} example pairs ({IntentCount} intent, {PlannerCount} planner) from {FeatureCount} feature(s) across {ModelCount} mock model(s).",
            primedExampleCount, intentExamples.Count, plannerExamples.Count, featureCount, primedModelCount);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    static (
        IReadOnlyList<(string Prompt, string Response)> Examples,
        IReadOnlyList<(string Transcript, string Response)> IntentExamples,
        IReadOnlyList<(string Intent, string Response)> PlannerExamples,
        int FeatureCount) LoadAllExamples()
    {
        var examples = new List<(string, string)>();
        var intentExamples = new List<(string, string)>();
        var plannerExamples = new List<(string, string)>();
        var featureCount = 0;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string[] resourceNames;
            try { resourceNames = asm.GetManifestResourceNames(); }
            catch { continue; }

            foreach (var name in resourceNames)
            {
                if (!name.EndsWith(".feature", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream);
                var featureText = reader.ReadToEnd();
                featureCount++;
                examples.AddRange(BddMockChatClient.ExtractExamples(featureText));
                intentExamples.AddRange(BddMockChatClient.ExtractIntentExamples(featureText));
                plannerExamples.AddRange(BddMockChatClient.ExtractPlannerExamples(featureText));
            }
        }
        return (examples, intentExamples, plannerExamples, featureCount);
    }
}
