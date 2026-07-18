using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using FluentAssertions;
using Xunit;
using DigitalBrain.SDK.DigitalBrain.Ai.GrokUiDesigner;

namespace DigitalBrain.InoLang.Tests
{
    public class GrokUiDesignerNeuronTests
    {
        [Fact]
        public async Task GrokUiDesignerNeuron_HandlesGrokUiDesignRequest_ReturnsBeautifulFallbackMockUi()
        {
            // Start TestDigitalBrain virtual Orleans silo with mocked LLM option
            var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
            try
            {
                var correlationId = Guid.NewGuid();
                
                // Construct GrokUiDesignRequest synapse
                var request = new GrokUiDesignRequest("Design a beautiful business dashboard")
                {
                    Headers = SynapseMetadata.Create(
                        synapseId: Guid.NewGuid(),
                        correlationId: correlationId,
                        causationId: Guid.Empty,
                        callerNeuronId: Guid.Empty,
                        callerNeuronType: "User",
                        receiverNeuronId: Guid.Empty,
                        receiverNeuronType: "GrokUiDesignerNeuron",
                        timestamp: DateTimeOffset.UtcNow
                    )
                };

                // Emit synapse into the virtual cluster
                await brain.Emit(request, TestContext.Current.CancellationToken);

                // Await corresponding GrokUiDesignResponse
                var response = await brain.AwaitSynapse<GrokUiDesignResponse>(correlationId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

                // Verify the response contains beautiful structured layout
                response.Should().NotBeNull();
                response.UiJson.Should().Contain("Corporate Command Center");
                response.UiJson.Should().Contain("UiKit.Column");
                response.Explanation.Should().Contain("Dashboard");
                response.InoCode.Should().Contain("UiKit.Card");
            }
            finally
            {
                await brain.DisposeAsync();
            }
        }

        [Fact]
        public async Task GrokUiDesignerNeuron_HandlesSaveUiToInoRequest_WritesFileToFS()
        {
            var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
            try
            {
                var correlationId = Guid.NewGuid();
                var cleanFilename = $"test_grok_designed_{DateTime.UtcNow.Ticks}";
                var inoCode = "ui:\n  UiKit.Card(title: \"Dashboard Test\")";

                // Construct SaveUiToInoRequest synapse
                var request = new SaveUiToInoRequest(InoCode: inoCode, Filename: cleanFilename)
                {
                    Headers = SynapseMetadata.Create(
                        synapseId: Guid.NewGuid(),
                        correlationId: correlationId,
                        causationId: Guid.Empty,
                        callerNeuronId: Guid.Empty,
                        callerNeuronType: "User",
                        receiverNeuronId: Guid.Empty,
                        receiverNeuronType: "GrokUiDesignerNeuron",
                        timestamp: DateTimeOffset.UtcNow
                    )
                };

                // Emit synapse into the virtual cluster
                await brain.Emit(request, TestContext.Current.CancellationToken);

                // Await corresponding SaveUiToInoResponse
                var response = await brain.AwaitSynapse<SaveUiToInoResponse>(correlationId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

                // Verify result
                response.Should().NotBeNull();
                response.Success.Should().BeTrue();
                response.ErrorMessage.Should().BeNull();

                // Double check that the file was written to FS
                var watchedDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../inolang"));
                var filePath = Path.Combine(watchedDir, $"{cleanFilename}.ino");
                File.Exists(filePath).Should().BeTrue();

                // Clean up the generated file
                try
                {
                    File.Delete(filePath);
                }
                catch { }
            }
            finally
            {
                await brain.DisposeAsync();
            }
        }
    }
}
