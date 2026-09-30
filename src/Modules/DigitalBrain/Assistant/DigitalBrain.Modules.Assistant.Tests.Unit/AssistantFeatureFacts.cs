using System.Text;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Specs;
using DigitalBrain.Supabase;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Assistant.Tests.Unit;

public sealed class AssistantFeatureFacts
{
    [Fact]
    public async Task TheAssistantFeatureIsGreen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<AssistantModule>().RequireModules([typeof(DigitalBrain.Apps.AppsModule), typeof(DigitalBrain.AI.AIModule), typeof(DigitalBrain.Compute.ComputeModule), typeof(DigitalBrain.Flutter.FlutterModule)])
            .WithModule<SpecsModule>()
            .WithModule<SupabaseModule>()
            .ConfigureSilo(silo => silo.Services
                .AddSingleton<IAiCredentials>(new FixedAiCredentials().Ready("openai", "fixture"))
                .AddSingleton<StepLibrary, AssistantSteps>()
                .Configure<AIOptions>(options => { options.Default.Provider = "OpenAI"; options.Default.Model = "fixture"; options.Default.Capabilities = LlmCapabilities.Tools; })
                .AddSingleton<InjectedModelTurnRunner>()
                .AddSingleton<IAgentTurnRunner>(services => services.GetRequiredService<InjectedModelTurnRunner>())
                .AddSingleton<IChatClient>(new ScriptedAssistantModel())
                .AddSingleton<CustomersDatabase>()
                .AddSingleton<ISupabaseProvider>(services => services.GetRequiredService<CustomersDatabase>())
                .AddSingleton<IAudioTranscriptionService>(new TextTranscriber()))
            .StartAsync(ct);
        var feature = brain.Get<IFeature>("assistant");
        var snapshot = await feature.Set(ReadFeature());
        Assert.True(snapshot.FullyBound, snapshot.Problem?.Message ?? "Some steps are not bound.");

        var run = await feature.Run("workspace-" + FeatureSnapshot.ScenarioPlaceholder + "/applications/assistant");

        Assert.True(run.Green, string.Join("\n", run.Scenarios
            .SelectMany(scenario => scenario.Steps.Where(step => step.Verdict != Verdict.Passed)
                .Select(step => $"{scenario.Name} line {step.Line}: {step.Verdict} {step.Message}"))));
    }

    private static string ReadFeature()
    {
        using var stream = typeof(AssistantFeatureFacts).Assembly.GetManifestResourceStream("assistant.feature")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Audio in these scenarios is the UTF-8 text the user "said".
    private sealed class TextTranscriber : IAudioTranscriptionService
    {
        public bool IsReady => true;
        public bool InitializationFailed => false;
        public string? ErrorMessage => null;
        public string ModelId => "text";
        public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
        {
            audioStream.Position = 44; // The fixture stores its transcript after a WAV header.
            using var reader = new StreamReader(audioStream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }
}
