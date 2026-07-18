using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using FluentAssertions;
using Xunit;
using DigitalBrain.SDK.DigitalBrain.Ai;

namespace DigitalBrain.InoLang.Tests;

public class LlmTranslationAndAlertingNeuronTests
{
    [Fact]
    public async Task LlmTranslationAndAlerting_E2EPipeline_SuccessfullyRoutesAndFiresCriticalAlert()
    {
        // Start TestDigitalBrain virtual Orleans silo with mocked LLM option
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var correlationId = Guid.NewGuid();
            
            // Construct TranslateTextRequest synapse (using "danger" to trigger Critical severity warning)
            var request = new TranslateTextRequest("There is danger ahead in this critical sector!", "Spanish")
            {
                Headers = SynapseMetadata.Create(
                    synapseId: Guid.NewGuid(),
                    correlationId: correlationId,
                    causationId: Guid.Empty,
                    callerNeuronId: Guid.Empty,
                    callerNeuronType: "User",
                    receiverNeuronId: Guid.Empty,
                    receiverNeuronType: "LlmTranslationNeuron",
                    timestamp: DateTimeOffset.UtcNow
                )
            };

            // Emit synapse into the virtual cluster
            await brain.Emit(request, TestContext.Current.CancellationToken);

            // Await corresponding SystemAlertFiredEvent
            var response = await brain.AwaitSynapse<SystemAlertFiredEvent>(
                correlationId, 
                TimeSpan.FromSeconds(10), 
                TestContext.Current.CancellationToken
            );

            // Verify the response contains beautiful structured layout
            response.Should().NotBeNull();
            response.Severity.Should().Be("Critical");
            response.AlertSummary.Should().Contain("Critical hostile sentiment detected");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }
}
