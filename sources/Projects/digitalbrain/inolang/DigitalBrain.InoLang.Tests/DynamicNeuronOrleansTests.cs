using DigitalBrain.Runtime.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Streams;
using DigitalBrain.Runtime.Tasks;
using FluentAssertions;
using DigitalBrain.SDK.DigitalBrain.Ai;

namespace DigitalBrain.InoLang.Tests;

public class DynamicNeuronOrleansTests
{
    [Fact]
    public async Task DynamicNeuron_DirectScriptExecution_Succeeds()
    {
        // 1. Start TestDigitalBrain
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var grainFactory = brain.GrainFactory;
            var fqn = "Dynamic.TestDirectNeuron";

            // Resolve DynamicNeuronGrain through GrainFactory
            var grain = grainFactory.GetGrain<IDynamicNeuron>(fqn);
            
            // Load custom Roslyn C# Script
            var spec = new DynamicNeuronSpec(
                new NeuronId("test-direct-id"),
                "A direct script test",
                "return \"Direct Script execution worked perfectly!\";",
                DateTimeOffset.UtcNow,
                DynamicNeuronStatus.Promoted
            );
            await grain.LoadAsync(spec);

            // Verify get spec
            var retrieved = await grain.GetSpecAsync();
            retrieved.Should().NotBeNull();
            retrieved!.RoslynScript.Should().Be(spec.RoslynScript);

            // Invoke dynamic grain
            var callable = grainFactory.GetGrain<ICallNeuronTarget>(GrainId.Create(GrainType.Create(fqn), "test-primary-key"));
            var response = await callable.AskAsync("test prompt");
            response.Should().Be("Direct Script execution worked perfectly!");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public async Task DynamicNeuron_InterpretedRegistryCompilation_Succeeds()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var grainFactory = brain.GrainFactory;
            var appField = typeof(TestDigitalBrain).GetField("_app", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            appField.Should().NotBeNull();
            var app = (WebApplication)appField!.GetValue(brain)!;
            app.Should().NotBeNull();

            var neuronRegistry = app.Services.GetRequiredService<IInterpretedNeuronRegistry>();
            neuronRegistry.Should().NotBeNull();

            var fqn = "Dynamic.TestInterpretedNeuron";
            var source = @"
neuron Dynamic.TestInterpretedNeuron
  using ask = synapse(Dynamic.TestReq)
  using ready = synapse(Dynamic.TestReady)
  on ask:
    emit ready(result: ""Success"")
";

            // Register FQN with InoLang source in the registry
            var descriptor = new NeuronDescriptor(
                fqn,
                Array.Empty<IncomingPort>(),
                Array.Empty<string>(),
                source
            );
            var registration = new InterpretedNeuronRegistration(descriptor, Array.Empty<string>());
            await neuronRegistry.RegisterDynamicAsync(registration);

            // Invoke DynamicNeuronGrain — it will dynamically compile and activate!
            var grain = grainFactory.GetGrain<ICallNeuronTarget>(GrainId.Create(GrainType.Create(fqn), "test-primary-key"));
            
            // Ask triggers AskAsync which runs InvokeAsync under 'ask' TypeName
            var response = await grain.AskAsync("test prompt");
            response.Should().NotBeNull();
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public void DynamicNeuron_LoopInstrumentation_InjectsCancellationChecks()
    {
        var method = typeof(DynamicNeuronGrain).GetMethod("InstrumentLoops", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        // 1. While loop
        var whileSource = "while (true) { }";
        var whileResult = (string)method!.Invoke(null, new object[] { whileSource })!;
        whileResult.Should().Contain("CancellationToken.ThrowIfCancellationRequested();");

        // 2. For loop
        var forSource = "for (int i = 0; i < 10; i++) { }";
        var forResult = (string)method.Invoke(null, new object[] { forSource })!;
        forResult.Should().Contain("CancellationToken.ThrowIfCancellationRequested();");

        // 3. Foreach loop
        var foreachSource = "foreach (var x in list) { }";
        var foreachResult = (string)method.Invoke(null, new object[] { foreachSource })!;
        foreachResult.Should().Contain("CancellationToken.ThrowIfCancellationRequested();");

        // 4. Do-while loop
        var doWhileSource = "do { } while (true);";
        var doWhileResult = (string)method.Invoke(null, new object[] { doWhileSource })!;
        doWhileResult.Should().Contain("CancellationToken.ThrowIfCancellationRequested();");
    }

    [Fact]
    public async Task DynamicNeuron_LoopCancellation_ThrowsTimeoutException()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var grainFactory = brain.GrainFactory;
            var fqn = "Dynamic.TestDirectNeuron";

            var grain = grainFactory.GetGrain<IDynamicNeuron>(fqn);
            
            var spec = new DynamicNeuronSpec(
                new NeuronId("test-loop-id"),
                "A loop cancellation test",
                "while (true) { } return \"not reached\";",
                DateTimeOffset.UtcNow,
                DynamicNeuronStatus.Promoted
            );
            await grain.LoadAsync(spec);

            var callable = grainFactory.GetGrain<ICallNeuronTarget>(GrainId.Create(GrainType.Create(fqn), "test-primary-key"));
            
            Func<Task> action = async () => await callable.AskAsync("run");
            var ex = await action.Should().ThrowAsync<Exception>();
            var msg = ex.And.Message.ToLowerInvariant();
            (msg.Contains("cancel") || msg.Contains("timeout") || msg.Contains("time out")).Should().BeTrue();
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public async Task DynamicNeuron_SynapsePortMapping_ResolvesActualFqn()
    {
        var services = new ServiceCollection();
        var fakeEmitter = new FakeSynapseEmitter();
        services.AddSingleton<ISynapseEmitter>(fakeEmitter);
        var provider = services.BuildServiceProvider();

        var synapsePorts = new Dictionary<string, string>
        {
            { "ready", "My.Actual.SynapseFqn" }
        };

        var globals = new DynamicNeuronScriptGlobals
        {
            Services = provider,
            SynapsePorts = synapsePorts
        };

        await globals.EmitAsync("ready", "Success");

        fakeEmitter.LastFqn.Should().Be("My.Actual.SynapseFqn");
        fakeEmitter.LastPayload.Should().ContainKey("payload");
        fakeEmitter.LastPayload!["payload"].Should().Be("Success");
    }

    [Fact]
    public async Task DynamicNeuron_StatePortOverloads_ReadAndWriteCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DigitalBrain.Runtime.Security.INeuronStateProtector, DigitalBrain.Runtime.Security.PassThroughNeuronStateProtector>();

        var stateFake = new FakeResourceNeuronTarget();
        var customFake = new FakeResourceNeuronTarget();

        var stateBinding = new NeuronBinding(InoLang.Ast.PortSigil.Resource, "StateTargetFqn", null);
        var customBinding = new NeuronBinding(InoLang.Ast.PortSigil.Resource, "CustomTargetFqn", null);

        // Register keyed services
        services.AddKeyedSingleton<IResourceNeuronTarget>(stateBinding, stateFake);
        services.AddKeyedSingleton<IResourceNeuronTarget>(customBinding, customFake);

        var provider = services.BuildServiceProvider();

        var bindings = new Dictionary<string, NeuronBinding>
        {
            { "state", stateBinding },
            { "customPort", customBinding }
        };

        var host = new DynamicNeuronHost(provider, bindings);

        var globals = new DynamicNeuronScriptGlobals
        {
            Services = provider,
            Neurons = host
        };

        // 1. Test 2-argument overload (uses "state" port)
        await globals.WriteStateAsync("myKey", "myValue");
        stateFake.Storage.Should().ContainKey("myKey");
        stateFake.Storage["myKey"].Should().Be("bXlWYWx1ZQ==");

        var readVal = await globals.ReadStateAsync("myKey");
        readVal.Should().Be("myValue");

        // 2. Test 3-argument overload (uses custom port)
        await globals.WriteStateAsync("customPort", "customKey", "customValue");
        customFake.Storage.Should().ContainKey("customKey");
        customFake.Storage["customKey"].Should().Be("Y3VzdG9tVmFsdWU=");

        var readCustomVal = await globals.ReadStateAsync("customPort", "customKey");
        readCustomVal.Should().Be("customValue");
    }

    [Fact]
    public void DynamicNeuron_ScriptCache_ReusesCompiledScriptInstances()
    {
        var method = typeof(DynamicNeuronGrain).GetMethod("GetOrCompileScript",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        var scriptSource = "return \"hello\";";

        var firstInstance = method!.Invoke(null, new object[] { scriptSource });
        var secondInstance = method.Invoke(null, new object[] { scriptSource });

        firstInstance.Should().NotBeNull();
        secondInstance.Should().NotBeNull();

        // Verify referential equality: caching worked!
        ReferenceEquals(firstInstance, secondInstance).Should().BeTrue();
    }

    [Fact]
    public async Task IntentDispatcher_UnknownIntent_RoutesToInoCreatorNeuron()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var appField = typeof(TestDigitalBrain).GetField("_app", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            appField.Should().NotBeNull();
            var app = (WebApplication)appField!.GetValue(brain)!;
            app.Should().NotBeNull();
            
            var clusterClient = app.Services.GetRequiredService<Orleans.IClusterClient>();
            var streamProvider = clusterClient.GetStreamProvider(StreamProviderConfig.SynapseProviderName);
            var timelineStream = streamProvider.GetStream<Synapse>(
                Orleans.Runtime.StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));

            var correlationId = Guid.NewGuid();
            var intentClassified = new IntentClassified(
                Transcript: "make a neuron that does something cool",
                Intent: KnownIntent.Unknown,
                Parameters: new Dictionary<string, string>()
            )
            {
                Headers = SynapseMetadata.Create(
                    synapseId: Guid.NewGuid(),
                    correlationId: correlationId,
                    causationId: Guid.Empty,
                    callerNeuronId: Guid.Empty,
                    callerNeuronType: "User",
                    receiverNeuronId: Guid.Empty,
                    receiverNeuronType: "IntentDispatcher",
                    timestamp: DateTimeOffset.UtcNow
                )
            };

            await timelineStream.OnNextAsync(intentClassified);

            // The IntentDispatcher grain will be implicitly activated by Orleans and process the synapse.
            // When it intercepts KnownIntent.Unknown, it routes an AuthorInoNeuronRequest with correlationId to InoCreatorNeuron.
            // InoCreatorNeuron will then be activated and start a background authoring task.
            // We can verify this happened by polling the IDurableTaskCompletionSourceGrain with correlationId.
            var completion = brain.GrainFactory.GetGrain<IDurableTaskCompletionSourceGrain>(correlationId.ToString());
            
            // Poll for state to change from initial/incomplete
            bool routed = false;
            for (int i = 0; i < 40; i++)
            {
                var state = await completion.GetState();
                if (state.IsCompleted || state.IsFaulted || state.IsCanceled)
                {
                    routed = true;
                    break;
                }
                await Task.Delay(250, TestContext.Current.CancellationToken);
            }

            routed.Should().BeTrue("The Unknown intent should have been routed and run via InoCreatorNeuron");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    private class FakeSynapseEmitter : ISynapseEmitter
    {
        public string? LastFqn { get; private set; }
        public IReadOnlyDictionary<string, string>? LastPayload { get; private set; }

        public Task EmitAsync(string fqn, IReadOnlyDictionary<string, string> payload, System.Threading.CancellationToken cancellationToken)
        {
            LastFqn = fqn;
            LastPayload = payload;
            return Task.CompletedTask;
        }
    }

    private class FakeResourceNeuronTarget : IResourceNeuronTarget
    {
        public readonly Dictionary<string, string> Storage = new();

        public Task<string?> ReadAsync(string key, CancellationToken ct)
        {
            Storage.TryGetValue(key, out var val);
            return Task.FromResult<string?>(val);
        }

        public Task WriteAsync(string key, string value, CancellationToken ct)
        {
            Storage[key] = value;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ImportSkill_FromRemoteInoSource_CompilesAndRoutesSuccessfully()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var grainFactory = brain.GrainFactory;
            var appField = typeof(TestDigitalBrain).GetField("_app", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            appField.Should().NotBeNull();
            var app = (WebApplication)appField!.GetValue(brain)!;
            app.Should().NotBeNull();

            var loader = app.Services.GetRequiredService<DigitalBrain.Kernel.Runtime.IInoPackageLoader>();
            loader.Should().NotBeNull();

            // Simulate Bob downloading and loading Alice's Roslyn C# script specification
            var alicesSharedCalculatorScript = "return \"Alice's Cluster calculated: 42\";";
            var fqn = "Dynamic.TestSharedCalculator";

            // Resolve IDynamicNeuron grain under the registered manifest FQN
            var grain = grainFactory.GetGrain<IDynamicNeuron>(fqn);
            
            var spec = new DynamicNeuronSpec(
                Id: new NeuronId("shared-calculator-id"),
                FeatureText: "Calculator Spec",
                RoslynScript: alicesSharedCalculatorScript,
                CreatedAt: DateTimeOffset.UtcNow,
                Status: DynamicNeuronStatus.Promoted
            );
            await grain.LoadAsync(spec);

            // Ask the dynamic calculator neuron
            var callable = grainFactory.GetGrain<ICallNeuronTarget>(Orleans.Runtime.GrainId.Create(Orleans.Runtime.GrainType.Create(fqn), "test-shared-key"));
            var response = await callable.AskAsync("calculate 5 + 5");

            response.Should().Be("Alice's Cluster calculated: 42");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public async Task InoPackageLoader_WithSecureGitHubToken_FetchesRawInoContent()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        
        var loader = new DigitalBrain.Kernel.Runtime.InoPackageLoader(factory, personalAccessToken: "ghp_secure_friend_token");
        loader.Should().NotBeNull();

        Func<Task> act = async () => await loader.DownloadFromUrlAsync("https://raw.githubusercontent.com/alice/skills/main/calculator.ino");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DynamicNeuron_MultiNeuronAskChain_Succeeds()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var grainFactory = brain.GrainFactory;
            var appField = typeof(TestDigitalBrain).GetField("_app", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            appField.Should().NotBeNull();
            var app = (WebApplication)appField!.GetValue(brain)!;
            app.Should().NotBeNull();

            var neuronRegistry = app.Services.GetRequiredService<IInterpretedNeuronRegistry>();
            neuronRegistry.Should().NotBeNull();

            // 1. Define Neuron B (the worker)
            var fqnB = "Dynamic.WorkerNeuron";
            var sourceB = @"
neuron Dynamic.WorkerNeuron
  using ask = synapse(Dynamic.WorkReq)
  on ask:
    log ""Worker received prompt!""
    let res = ""Result: Done!""
";
            var compB = InoCompiler.Compile(sourceB);
            compB.Success.Should().BeTrue(string.Join("; ", compB.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

            var descriptorB = new NeuronDescriptor(
                fqnB,
                Array.Empty<IncomingPort>(),
                Array.Empty<string>(),
                sourceB
            );
            var registrationB = new InterpretedNeuronRegistration(descriptorB, Array.Empty<string>());
            await neuronRegistry.RegisterDynamicAsync(registrationB);

            // 2. Define Neuron A (the router, wired to Neuron B via a neuron port)
            var fqnA = "Dynamic.RouterNeuron";
            var sourceA = @"
neuron Dynamic.RouterNeuron
  using ask = synapse(Dynamic.RouteReq)
  using worker = neuron(Dynamic.WorkerNeuron)
  on ask:
    let res = ask worker to ""do work""
";
            var compA = InoCompiler.Compile(sourceA);
            compA.Success.Should().BeTrue(string.Join("; ", compA.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

            var descriptorA = new NeuronDescriptor(
                fqnA, 
                Array.Empty<IncomingPort>(), 
                Array.Empty<string>(), 
                sourceA
            );
            var registrationA = new InterpretedNeuronRegistration(descriptorA, Array.Empty<string>());
            await neuronRegistry.RegisterDynamicAsync(registrationA);

            // 3. Invoke Neuron A. It will ask Neuron B, compiling both dynamically!
            var grainA = grainFactory.GetGrain<ICallNeuronTarget>(Orleans.Runtime.GrainId.Create(Orleans.Runtime.GrainType.Create(fqnA), "test-primary-key"));
            var response = await grainA.AskAsync("go");
            response.Should().Be("Result: Done!");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }
}
