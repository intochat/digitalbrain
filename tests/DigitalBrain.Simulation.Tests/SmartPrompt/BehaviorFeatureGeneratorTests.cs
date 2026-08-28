using DigitalBrain.SmartPrompt;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

public sealed class BehaviorFeatureGeneratorTests
{
    [Fact]
    public async Task Provider_failure_uses_validated_correction_fallback()
    {
        var compiler = BehaviorCompiler.CreateDefault();
        var generator = new BehaviorFeatureGenerator(new FailingChatClient(), compiler);

        var generated = await generator.GenerateCorrection(
            SalesforceAccountEnrichment,
            "Preserve human-verified Salesforce fields when enriching accounts.",
            TestContext.Current.CancellationToken);

        Assert.True(generated.Compilation.Success);
        Assert.Contains(
            "preserve verified Salesforce fields",
            generated.Source,
            StringComparison.OrdinalIgnoreCase);

        var parent = Assert.IsType<BehaviorPlan>(compiler.Compile(SalesforceAccountEnrichment).Plan);
        var candidate = Assert.IsType<BehaviorPlan>(generated.Compilation.Plan);
        var validation = BehaviorTestInterpreter.ValidateCorrectionCandidate(candidate, parent);
        Assert.True(validation.StructurallyValid);
        Assert.False(validation.ParentReport.AllGreen);
        Assert.True(BehaviorTestInterpreter.Validate(candidate, generated.Compilation.Diagnostics).AllGreen);
    }

    private sealed class FailingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(new HttpRequestException("Ollama returned 500."));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private const string SalesforceAccountEnrichment =
        """
        Feature: Salesforce account enrichment
          @behavior
          Scenario: Enrich Salesforce account from a new company email
            Given Email.Account("vlad@intochat.io")
            When a new Email is received
            Then research the sender company with Web.Agent
            And enrich Salesforce.Account with verified company research through MCP
            And notify UI.Chat("main")
          @test
          Scenario: IntoChat email enriches its Salesforce account
            Given fake event "email.received" from "vlad@intochat.io" with text "new company email from IntoChat" and value 1
            When behavior "Enrich Salesforce account from a new company email" runs
            Then UI.Chat("main") contains a behavior notification
        """;
}
