using DigitalBrain.Core;
using DigitalBrain.Ino;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Ino;

public sealed class InoNeuronToolCallSynapseTests : NeuronTestBase
{
    [Fact]
    public async Task Fires_new_Ino_tool_and_auth_synapses_and_they_appear_in_timeline()
    {
        var ino = Grain<IInoNeuron>("ino-toolcall-test");

        // Fire the new Phase 0 synapses (as would be done from InoNeuron and providers).
        await ino.FireAsync(new InoToolCallStarted("gmail_get_messages", "google", "session-toolcall"));
        await ino.FireAsync(new InoToolCallCompleted("gmail_get_messages", "Gmail: MessageId:123; Snippet:hello", "google", "session-toolcall"));
        await ino.FireAsync(new InoToolCallFailed("salesforce_query", "timeout", "salesforce", "session-toolcall"));
        await ino.FireAsync(new InoConnectorAuthRequired("google", "session-toolcall", null, "sign-in prompt shown"));

        var timeline = await ino.GetOutgoingTimelineAsync();

        Assert.Contains(timeline, s => s is InoToolCallStarted t && t.ToolName == "gmail_get_messages" && t.Provider == "google");
        Assert.Contains(timeline, s => s is InoToolCallCompleted c && c.ToolName == "gmail_get_messages");
        Assert.Contains(timeline, s => s is InoToolCallFailed f && f.ToolName == "salesforce_query");
        Assert.Contains(timeline, s => s is InoConnectorAuthRequired a && a.Provider == "google");
    }
}
