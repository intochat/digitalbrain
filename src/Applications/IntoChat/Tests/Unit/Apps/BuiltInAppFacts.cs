using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Testing.Unit;
using IntoChat.Agent;
using IntoChat.Apps.BuiltIn;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntoChat.Tests;

public sealed class BuiltInAppFacts
{
    [Fact]
    public async Task SettingsOwnsEditableNeuronsAndPreservesPreferencesAcrossActivation()
    {
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().AddApp<SettingsApp>()
            .ConfigureSilo(silo => silo.Services.Configure<BrainOptions>(options =>
            {
                options.ObserverLease = TimeSpan.FromSeconds(2);
                options.RenewEvery = TimeSpan.FromMilliseconds(200);
                options.OperationTimeout = TimeSpan.FromMilliseconds(500);
            })).StartAsync(TestContext.Current.CancellationToken);
        var first = brain.Get<ISettingsApp>("workspace-a");
        var second = brain.Get<ISettingsApp>("workspace-b");
        var opened = await first.Activate();
        var other = await second.Activate();
        await brain.Get<ITextField>(opened.DisplayNameField.Name).SetValue("Alice");
        await brain.Get<ITextField>(opened.ThemeField.Name).SetValue("dark");
        // A settings window must remain wired after the original observer lease expires.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        await brain.Get<IButton>("workspace-a/apps/settings/apply").Click();
        var applied = await first.Read();
        var reopened = await first.Activate();

        Assert.Equal(new SettingsPreferences("Alice", "dark"), applied.Preferences);
        Assert.Equal(applied.Revision, reopened.Revision);
        Assert.Equal("Alice", (await brain.Get<ITextField>(reopened.DisplayNameField.Name).Read()).Value);
        Assert.Equal(new SettingsPreferences(), (await second.Read()).Preferences);
        Assert.NotEqual(opened.Surface.Name, other.Surface.Name);
        Assert.NotEmpty((await brain.Get<ISurface>(opened.Surface.Name).Read()).Definition.Children);
        await Assert.ThrowsAsync<ArgumentException>(() => first.Apply(new SettingsPreferences("Alice", "invalid")));
        Assert.Equal(applied.Preferences, (await first.Read()).Preferences);
    }

    [Fact]
    public async Task AssistantRunsItsConfiguredAgentAndKeepsWorkspaceConversationsIndependent()
    {
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().WithModule<AIModule>().AddApp<AssistantApp>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(new FixedChatClient()))
            .StartAsync(TestContext.Current.CancellationToken);
        var first = brain.Get<IAssistantApp>("workspace-a");
        var second = brain.Get<IAssistantApp>("workspace-b");
        var opened = await first.Activate();
        var configured = await first.Configure(new AgentDefinition { DisplayName = "Research assistant", Instructions = "Be concise." });
        await brain.Get<ITextField>(opened.Input.Name).SetValue("hello");
        await brain.Get<IButton>("workspace-a/apps/assistant/send").Click();

        Assert.Equal("A real agent response", (await brain.Get<IText>(opened.Response.Name).Read()).Markdown);
        Assert.Equal("Research assistant", (await first.Activate()).Agent.DisplayName);
        Assert.Equal(configured.Revision, (await first.Read()).Revision);
        Assert.Equal("Assistant", (await second.Activate()).Agent.DisplayName);
        var conversation = await first.Conversation("thread-1");
        await conversation.BeginConversation(new("run-1", "hello"), TestContext.Current.CancellationToken);
        await conversation.CompleteConversation(new("run-1", "hello", "answer", []), TestContext.Current.CancellationToken);
        Assert.Single((await brain.Get<IAgent>(AgentEndpoints.ConversationKey("workspace-a", "thread-1")).ReadConversation(TestContext.Current.CancellationToken)).Turns);
        Assert.Empty((await (await second.Conversation("thread-1")).ReadConversation(TestContext.Current.CancellationToken)).Turns);
        Assert.NotEqual(opened.Surface.Name, (await second.Read()).Surface.Name);
        await Assert.ThrowsAsync<ArgumentException>(() => first.Conversation("foreign/thread"));
    }

    private sealed class FixedChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A real agent response")));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
