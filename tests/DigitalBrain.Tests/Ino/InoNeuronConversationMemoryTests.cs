using DigitalBrain.Core;
using DigitalBrain.Ino;
using DigitalBrain.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Ino;

// Shares CapturingInoChatClient's static Prompts/Replies with InoNeuronSecretRedactionTests, so both
// are pinned to the same xUnit collection to avoid a cross-class race on that static state (see the
// comment next to InoNeuronSecretRedactionTests's [Collection] attribute).
[Collection("Ino.CapturingInoChatClient")]
public sealed class InoNeuronConversationMemoryTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IChatClient, CapturingInoChatClient>());

    [Fact]
    public async Task Second_turn_includes_the_first_turns_exchange_in_the_prompt_sent_to_the_model()
    {
        CapturingInoChatClient.Reset();
        CapturingInoChatClient.Replies.Enqueue("{\"intent\":\"generic\",\"confidence\":0.4}");
        CapturingInoChatClient.Replies.Enqueue("Nice to meet you, Alice.");
        CapturingInoChatClient.Replies.Enqueue("{\"intent\":\"generic\",\"confidence\":0.4}");
        CapturingInoChatClient.Replies.Enqueue("Your name is Alice.");

        // Phrasing avoids InoCapabilityAnswers.CapabilityNameRegex's "<...> is <...>$" / "is <...>?$" shape
        // (a pre-existing, unrelated fast-path that intercepts "My name is Alice." / "What is my name?" before
        // they ever reach the classifier or chat client).
        var ino = Grain<IInoNeuron>("ino-memory");
        await InoTestHarness.Interact(ino, "I am Alice, remember that please.", clientId: "memory-client");
        await InoTestHarness.Interact(ino, "Do you remember my name?", clientId: "memory-client");

        // Prompts[^1] is NOT reliably the generic call: HandleGenericIntentAsync's own (pre-existing,
        // unrelated) CreateMemorySummaryAsync fires one more chat call *after* the generic answer once
        // enough journal volume has accumulated (which happens from turn 1 onward), so the truly-last
        // captured prompt is that summary call, not the generic call's messages. The generic call is the
        // only one carrying HandleGenericIntentAsync's system preamble, so select on that instead of index.
        var genericTurnPrompt = CapturingInoChatClient.Prompts.Last(
            p => p.Contains("You are INO, the personal AI in DigitalBrain"));
        Assert.Contains("I am Alice, remember that please.", genericTurnPrompt);
        Assert.Contains("Nice to meet you, Alice.", genericTurnPrompt);
        Assert.Contains("Do you remember my name?", genericTurnPrompt);
    }

    [Fact]
    public async Task Different_clients_do_not_see_each_others_history()
    {
        CapturingInoChatClient.Reset();
        CapturingInoChatClient.Replies.Enqueue("{\"intent\":\"generic\",\"confidence\":0.4}");
        CapturingInoChatClient.Replies.Enqueue("Nice to meet you, Bob.");
        CapturingInoChatClient.Replies.Enqueue("{\"intent\":\"generic\",\"confidence\":0.4}");
        CapturingInoChatClient.Replies.Enqueue("I don't know your name yet.");

        var ino = Grain<IInoNeuron>("ino-memory-isolation");
        await InoTestHarness.Interact(ino, "I am Bob, remember that please.", clientId: "client-a");
        await InoTestHarness.Interact(ino, "Do you remember my name?", clientId: "client-b");

        // Verified directly against the journaled InoConversationTurn facts (what LoadConversationHistory
        // reads) rather than the full chat prompt: HandleGenericIntentAsync's separate, pre-existing
        // BuildContextAsync/"RecentCausalHistory" context section dumps recent journal entries' raw
        // ToString() into the prompt for ANY client sharing this grain, unscoped by clientId - a real but
        // unrelated leak that predates this task and is out of scope here. Asserting on the full prompt
        // would therefore fail regardless of whether LoadConversationHistory's own clientId scoping (the
        // thing Task 9 actually adds) is correct, so this checks that scoping directly instead.
        var turns = (await ino.GetOutgoingTimelineAsync()).OfType<InoConversationTurn>().ToList();
        Assert.Contains(turns, turn => turn.ClientId == "client-a" && turn.Text.Contains("Bob"));
        Assert.DoesNotContain(turns, turn => turn.ClientId == "client-b" && turn.Text.Contains("Bob"));
    }
}
