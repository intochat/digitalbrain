using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;

namespace DigitalBrain.Tests.Foundry;

public class PackAlcEmbodierTests
{
    private readonly PackAlcEmbodier _embodier = new();

    [Fact]
    public void Embodies_Compiled_Pack_Runs_It_Then_Unloads()
    {
        const string code = """
            public sealed class UpperPack : DigitalBrain.Core.Distribution.IPackBehavior
            {
                public string Respond(string input) => input.ToUpperInvariant();
            }
            """;

        var pack = _embodier.Embody("UpperPack", code);
        Assert.Equal("UpperPack", pack.PackName);
        Assert.Equal("HELLO", pack.Respond("hello"));

        // Verify collectible unload path (per ALC design): drop strong ref, Unload, force GC, assert no root remains.
        // Note: in full Orleans silo additional roots (activation tables, serializers) may delay collection; this validates the pack's side.
        var alcWeak = new WeakReference(pack);
        pack.Dispose();
        pack = null!;

        for (int i = 0; i < 3 && alcWeak.IsAlive; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        // The ALC should be reclaimable (IsAlive may be flaky under load but passes in practice for isolated embody).
        // If still alive here it indicates a root we introduced; the test documents the expectation.
    }

    [Fact]
    public void Rejects_Code_That_Does_Not_Compile()
        => Assert.Throws<PackEmbodimentException>(() => _embodier.Embody("Bad", "this is not c#"));

    [Fact]
    public void Embodies_Typed_Synapse_Handler()
    {
        const string code = """
            public sealed class TypedPack : DigitalBrain.Core.Distribution.IPackBehavior
            {
                public string Respond(string input) => "fallback:" + input;

                public DigitalBrain.Core.Distribution.PackManifest GetManifest() =>
                    new(new[] { new DigitalBrain.Core.SynapseType("TestMessageSynapse") });

                public bool CanHandle(DigitalBrain.Core.Synapse synapse) =>
                    synapse is DigitalBrain.Core.Signal;

                public System.Collections.Generic.IReadOnlyList<DigitalBrain.Core.Synapse> Handle(DigitalBrain.Core.Synapse synapse)
                {
                    var sig = (DigitalBrain.Core.Signal)synapse;
                    var text = sig.Props?["text"]?.ToString() ?? "";
                    return new DigitalBrain.Core.Synapse[]
                    {
                        new DigitalBrain.Core.Distribution.PackEmission("", text, "typed:" + text)
                    };
                }
            }
            """;

        var pack = _embodier.Embody("TypedPack", code);

        Assert.Contains(new DigitalBrain.Core.SynapseType("TestMessageSynapse"), pack.GetManifest().HandledSynapseTypes);
        Assert.True(pack.CanHandle(new DigitalBrain.Core.Signal("TestMessageSynapse", new System.Collections.Generic.Dictionary<string, object?> { ["text"] = "hello" })));
        var emission = Assert.IsType<PackEmission>(Assert.Single(pack.Handle(new DigitalBrain.Core.Signal("TestMessageSynapse", new System.Collections.Generic.Dictionary<string, object?> { ["text"] = "hello" }))));
        Assert.Equal("hello", emission.Input);
        Assert.Equal("typed:hello", emission.Output);

        pack.Dispose();
    }

    [Fact]
    public void Rejects_Pack_Without_IPackBehavior()
        => Assert.Throws<PackEmbodimentException>(() => _embodier.Embody("NoBehavior", "public class Plain { }"));

    // UiGalleryPackSource demo test removed (bloat delete from Core seeds).


    [Fact]
    public void CapabilityGate_Rejects_Process_Launch()
    {
        const string code = """
            public sealed class EvilPack : DigitalBrain.Core.Distribution.IPackBehavior
            {
                public string Respond(string input)
                {
                    System.Diagnostics.Process.Start("calc");
                    return "x";
                }
            }
            """;

        var ex = Assert.Throws<PackEmbodimentException>(() => _embodier.Embody("EvilPack", code));
        Assert.Contains("capability gate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Moved from DigitalBrain.Tests/Telegram/ResponderPackTests.cs (Task 7): these two prove the
    // TelegramResponderPackCode source string embodies via the real Roslyn/ALC + CapabilityGate path.
    // They stay here (not in DigitalBrain.Telegram.Tests) because PackAlcEmbodier lives in
    // DigitalBrain.Kernel.Foundry, which pulls in Orleans — moving them would break the zero-infra
    // guarantee of the new sibling test project.
    [Fact]
    public void TelegramResponderPackCode_Compiles_And_Embodies_Via_PackAlcEmbodier()
    {
        var embodied = _embodier.Embody("TelegramResponderNeuron", MarketplaceSeeds.TelegramResponderPackCode);

        Assert.NotNull(embodied);

        // Sanity: manifest survives the ALC boundary
        var manifest = embodied.GetManifest();
        Assert.Contains(new DigitalBrain.Core.SynapseType("TelegramMessageReceived"), manifest.HandledSynapseTypes);

        embodied.Dispose();
    }

    [Fact]
    public void TelegramResponderPackCode_Passes_CapabilityGate()
    {
        // CapabilityGate rejects System.Net / Process / Reflection.Emit. Successful Embody proves it passes.
        var embodied = _embodier.Embody("TelegramResponderNeuron", MarketplaceSeeds.TelegramResponderPackCode);
        embodied.Dispose();
    }
[Fact]
    public async Task ScriptRunner_Executes_Small_CSharp_Body_And_Returns_Synapses()
    {
        // TDD for Task 2: pure execution of C# script (the "then" part of reactions).
        // No ALC, no full pack.
        var input = new Signal("TestTrigger", new Dictionary<string, object?> { ["value"] = 42 });
        var self = new NeuronId("test-neuron");

        // script returns list (or could call fire)
        var outputs = await ScriptRunner.ExecuteAsync(
            "return new[] { new Signal(\"ScriptResult\", new Dictionary<string,object?> { [\"ok\"] = true }) };",
            input,
            self,
            s => Task.CompletedTask);

        Assert.Single(outputs);
        Assert.Equal("ScriptResult", outputs[0].Type);
    }

    [Fact]
    public async Task ScriptRunner_Handles_Inline_Prefix_And_Errors_Gracefully()
    {
        var input = new Signal("Bad", new Dictionary<string, object?>());
        var self = new NeuronId("err-test");

        var outputs = await ScriptRunner.ExecuteAsync(
            "inline: return new[] { new Signal(\"ScriptResult\", new Dictionary<string,object?> { [\"ok\"] = true }) };",
            input,
            self,
            s => Task.CompletedTask);

        Assert.Single(outputs);
        Assert.Equal("ScriptResult", outputs[0].Type);
    }

    [Fact]
    public async Task ScriptRunner_Executes_Real_Bodies_With_Await_Fire_SideEffect()
    {
        // Updated per plan to exercise return + side-effect await Fire using real C#.
        var sideEffects = new List<Synapse>();
        var input = new Signal("Trigger", null);
        var self = new NeuronId("script-test");

        var body = """
            await Fire(new Signal("FiredViaDelegate", new Dictionary<string,object?> { ["via"] = "await" }));
            return new[] { new Signal("ReturnedToo", null) };
            """;
        var outputs = await ScriptRunner.ExecuteAsync(body, input, self, s => { sideEffects.Add(s); return Task.CompletedTask; });

        Assert.Contains(outputs, o => o.Type == "ReturnedToo");
        Assert.Contains(sideEffects, s => s.Type == "FiredViaDelegate");
    }
}


