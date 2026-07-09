using DigitalBrain.Core;
using DigitalBrain.Ino;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Ino;

public sealed class InoNeuronToolCapableModelResolutionTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
                ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "test-provider",
                ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "chat-only-test",
                ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "test-provider-chat-only-test",
                ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Balanced",
                ["DigitalBrain:ModelRegistry:Registrations:0:SupportsTools"] = "false",
                ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = "LargeLanguageModel",
                ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = "ollama",
                ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "llama3.1:8b",
                ["DigitalBrain:ModelRegistry:Registrations:1:ServiceKey"] = "ollama-llama3-1-8b",
                ["DigitalBrain:ModelRegistry:Registrations:1:Role"] = "Reasoning",
                ["DigitalBrain:ModelRegistry:Registrations:1:SupportsTools"] = "true",
            }).Build();
            services.AddSingleton<IConfiguration>(config);
            services.AddDigitalBrainChatClients(config);
            // The "flat default" IChatClient is deliberately the chat-only model here, so this test can
            // prove Ino picks the tool-capable one instead of just grabbing whatever the unkeyed default is.
            services.AddKeyedSingleton<IChatClient>("test-provider-chat-only-test", new RecordingChatClient("chat-only"));
            services.AddKeyedSingleton<IChatClient>("ollama-llama3-1-8b", new RecordingChatClient("tool-capable"));
        });

    [Fact]
    public async Task GenericIntentPathResolvesTheToolCapableRegisteredModelOverTheFlatDefault()
    {
        var ino = Grain<IInoNeuron>("ino-tool-capable");
        await ino.FireAsync(new InoRequest("tell me a joke", "session-tool-capable"));

        var response = (await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>().Last();
        Assert.Contains("tool-capable", response.Response);
    }

    private sealed class RecordingChatClient(string tag) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, tag)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
