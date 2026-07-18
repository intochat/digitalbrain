using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Runtime;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Security;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Orleans.Journaling;
using DigitalBrain.Runtime.Diagnostics;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime;

// Globals exposed to a dynamic neuron's Roslyn script. The script body has
// access to PayloadJson (incoming synapse payload as JSON), TypeName (incoming
// synapse FQN, so the script can branch by type), CorrelationId (the
// conversation correlation), and Services (silo IServiceProvider, so the script
// can resolve IChatClient, fire other neurons, etc. — slice 2 wires this richer).
// The script returns a string (the response payload as JSON).
public sealed class DynamicNeuronScriptGlobals
{
    public string PayloadJson { get; init; } = "";
    public string TypeName { get; init; } = "";
    public CorrelationId CorrelationId { get; init; }
    public IServiceProvider Services { get; init; } = null!;
    public INeuronHost Neurons { get; init; } = null!;
    public System.Threading.CancellationToken CancellationToken { get; init; }
    public IReadOnlyDictionary<string, string> SynapsePorts { get; init; } = null!;

    public async Task<string> AskAsync(string port, string prompt)
    {
        Console.WriteLine($"[DIAGNOSTIC-GLOBALS] AskAsync called for port '{port}', prompt '{prompt}'. CorrelationId = '{RequestContext.Get("DigitalBrain.CorrelationId")}', ActiveScope = '{RequestContext.Get("DigitalBrain.ActiveScope")}'");
        return await Neurons.AskAsync(port, prompt, CancellationToken.None);
    }

    public async Task EmitAsync(string port, object payload)
    {
        var emitter = Services.GetRequiredService<ISynapseEmitter>();
        IReadOnlyDictionary<string, string> dict;
        if (payload is IReadOnlyDictionary<string, string> roDict)
        {
            dict = roDict;
        }
        else if (payload is IDictionary<string, string> rwDict)
        {
            dict = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(rwDict);
        }
        else if (payload is string s)
        {
            dict = new Dictionary<string, string> { { "payload", s } };
        }
        else if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            dict = parsed?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "") ?? new();
        }
        else
        {
            dict = new Dictionary<string, string>();
        }

        var actualPort = port;
        if (SynapsePorts != null && SynapsePorts.TryGetValue(port, out var fqn))
        {
            actualPort = fqn;
        }
        await emitter.EmitAsync(actualPort, dict, CancellationToken.None);
    }

    public async Task<string?> ReadStateAsync(string port, string key)
    {
        if (Neurons is DynamicNeuronHost host)
        {
            var raw = await host.ReadResourceAsync(port, key, CancellationToken.None);
            if (string.IsNullOrEmpty(raw)) return raw;
            try
            {
                var protector = Services.GetRequiredService<INeuronStateProtector>();
                var bytes = Convert.FromBase64String(raw);
                var decrypted = protector.Unprotect(bytes);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return raw;
            }
        }
        return null;
    }

    public async Task<string?> ReadStateAsync(string key)
    {
        return await ReadStateAsync("state", key);
    }

    public async Task WriteStateAsync(string port, string key, string value)
    {
        var protector = Services.GetRequiredService<INeuronStateProtector>();
        var bytes = Encoding.UTF8.GetBytes(value);
        var encrypted = protector.Protect(bytes);
        var base64 = Convert.ToBase64String(encrypted);

        if (Neurons is DynamicNeuronHost host)
        {
            await host.WriteResourceAsync(port, key, base64, CancellationToken.None);
        }
    }

    public async Task WriteStateAsync(string key, string value)
    {
        await WriteStateAsync("state", key, value);
    }

    public string GetField(string fieldName)
    {
        if (string.IsNullOrEmpty(PayloadJson)) return "";
        try
        {
            var trimmed = PayloadJson.TrimStart();
            if (trimmed.StartsWith("{"))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(PayloadJson);
                if (dict != null && dict.TryGetValue(fieldName, out var elem))
                {
                    return elem.ValueKind == JsonValueKind.String ? elem.GetString() ?? "" : elem.GetRawText();
                }
            }
            return PayloadJson;
        }
        catch
        {
            return PayloadJson;
        }
    }

    public string FormatCallExpr(string builtin, string argVal)
    {
        switch (builtin)
        {
            case "is-successful-spawn":
                return argVal.StartsWith("success:", StringComparison.Ordinal) ? "true" : "false";
            case "get-token-from-spawn":
                return argVal.StartsWith("success:", StringComparison.Ordinal) ? argVal["success:".Length..] : "";
            case "is-azure":
                return string.Equals(argVal, "azure", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            case "is-consent-required":
                return string.Equals(argVal, "OAuthConsentRequired", StringComparison.Ordinal) ? "true" : "false";
            case "extract-path":
            case "get-folder-path":
                var pathMatch = System.Text.RegularExpressions.Regex.Match(argVal, @"([a-zA-Z]:[/\\][^""\s]+|""(?<path>[^""]+)""|(?<path>[a-zA-Z]:[/\\]\S+))");
                if (pathMatch.Success)
                {
                    return pathMatch.Value.Trim('"').Replace("\\", "/");
                }
                if (argVal.Contains("D:/"))
                {
                    return "D:/" + argVal.Split("D:/")[1].Split(' ')[0].Trim('"');
                }
                return argVal;
            default:
                return string.IsNullOrEmpty(argVal) ? builtin : $"{builtin} {argVal}";
        }
    }
}

[GrainType("DynamicNeuronGrain")]
public sealed class DynamicNeuronGrain(
    [FromKeyedServices("dynamic-neuron-spec")] IDurableValue<DynamicNeuronSpec> spec,
    IServiceProvider services)
    : DurableGrain, IDynamicNeuron, ICallNeuronTarget, IStreamNeuronTarget, IResourceNeuronTarget, IPredicateNeuronTarget
{
    Script<string>? _compiled;
    string? _fqn;
    IReadOnlyDictionary<string, NeuronBinding>? _neuronBindings;
    IReadOnlyDictionary<string, string>? _synapsePorts;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var grainTypeStr = this.GrainReference.GrainId.Type.ToString() ?? "";
        if (grainTypeStr != "DynamicNeuronGrain" && grainTypeStr.Contains('.'))
        {
            _fqn = grainTypeStr;
        }
        else
        {
            _fqn = this.GetPrimaryKeyString();
        }

        if (spec.Value is { } loaded)
        {
            _compiled = GetOrCompileScript(loaded.RoslynScript);
        }
        else
        {
            var primaryKey = this.GetPrimaryKeyString();
            if (_fqn != primaryKey)
            {
                try
                {
                    var grainFactory = services.GetRequiredService<IGrainFactory>();
                    var configGrain = grainFactory.GetGrain<IDynamicNeuron>(_fqn);
                    var fetchedSpec = await configGrain.GetSpecAsync();
                    if (fetchedSpec != null)
                    {
                        spec.Value = fetchedSpec;
                        _compiled = GetOrCompileScript(fetchedSpec.RoslynScript);
                    }
                }
                catch {}
            }

            if (_compiled == null)
            {
            // E-SDK #63. Lookup in InterpretedNeuronRegistry to enable instantaneous activation for .ino files dynamically via reflection to avoid project reference cycle
            var registry = GetServiceDynamically(services, "DigitalBrain.Runtime.Runtime.IInterpretedNeuronRegistry");
            if (registry != null)
            {
                var tryGetMethod = registry.GetType().GetMethod("TryGet");
                if (tryGetMethod != null)
                {
                    var args = new object?[] { _fqn, null };
                    var found = (bool)tryGetMethod.Invoke(registry, args)!;
                    if (found && args[1] is { } registration)
                    {
                        var descriptorProp = registration.GetType().GetProperty("Descriptor");
                        var descriptor = descriptorProp?.GetValue(registration);
                        if (descriptor != null)
                        {
                            var inoLangSourceProp = descriptor.GetType().GetProperty("InoLangSource");
                            string source = inoLangSourceProp?.GetValue(descriptor) as string ?? "";

                            var cache = GetServiceDynamically(services, "DigitalBrain.Kernel.Runtime.InoDefinitionCache");
                            if (string.IsNullOrEmpty(source) && cache != null)
                            {
                                var getSourceMethod = cache.GetType().GetMethod("GetSourceAsync");
                                if (getSourceMethod != null)
                                {
                                    try
                                    {
                                        var taskVal = getSourceMethod.Invoke(cache, new object[] { descriptor, cancellationToken });
                                        if (taskVal is ValueTask<string> vt)
                                        {
                                            source = await vt;
                                        }
                                        else if (taskVal != null)
                                        {
                                            var asTaskMethod = taskVal.GetType().GetMethod("AsTask");
                                            if (asTaskMethod != null)
                                            {
                                                var task = (Task<string>)asTaskMethod.Invoke(taskVal, null)!;
                                                source = await task;
                                            }
                                        }
                                    }
                                    catch {}
                                }
                            }

                            if (!string.IsNullOrEmpty(source))
                            {
                                var catalog = DeferredContractCatalog.Instance;
                                var compiled = InoCompiler.Compile(source, catalog);
                                if (compiled.Success && compiled.Plan != null)
                                {
                                    var scriptSource = InoToScriptTranspiler.Transpile(compiled.Plan);
                                    var newSpec = new DynamicNeuronSpec(
                                        Id: new NeuronId(Guid.NewGuid().ToString()),
                                        FeatureText: "",
                                        RoslynScript: scriptSource,
                                        CreatedAt: DateTimeOffset.UtcNow,
                                        Status: DynamicNeuronStatus.Promoted
                                    );
                                    spec.Value = newSpec;
                                    _compiled = GetOrCompileScript(scriptSource);
                                    _synapsePorts = compiled.Plan.SynapsePorts;
                                }
                            }
                        }
                    }
                }
            }
        }
        }

        await base.OnActivateAsync(cancellationToken);
        await ResolveBindingsAsync(cancellationToken);
    }

    private async Task ResolveBindingsAsync(CancellationToken cancellationToken)
    {
        _neuronBindings = null;
        _synapsePorts = null;
        var registry = GetServiceDynamically(services, "DigitalBrain.Runtime.Runtime.IInterpretedNeuronRegistry");
        if (registry != null)
        {
            var tryGetMethod = registry.GetType().GetMethod("TryGet");
            if (tryGetMethod != null)
            {
                var args = new object?[] { _fqn, null };
                var found = (bool)tryGetMethod.Invoke(registry, args)!;
                if (found && args[1] is { } registration)
                {
                    var descriptorProp = registration.GetType().GetProperty("Descriptor");
                    var descriptor = descriptorProp?.GetValue(registration);
                    if (descriptor != null)
                    {
                        var inoLangSourceProp = descriptor.GetType().GetProperty("InoLangSource");
                        string source = inoLangSourceProp?.GetValue(descriptor) as string ?? "";

                        var cache = GetServiceDynamically(services, "DigitalBrain.Kernel.Runtime.InoDefinitionCache");
                        if (string.IsNullOrEmpty(source) && cache != null)
                        {
                            var getSourceMethod = cache.GetType().GetMethod("GetSourceAsync");
                            if (getSourceMethod != null)
                            {
                                try
                                {
                                    var taskVal = getSourceMethod.Invoke(cache, new object[] { descriptor, cancellationToken });
                                    if (taskVal is ValueTask<string> vt)
                                    {
                                        source = await vt;
                                    }
                                    else if (taskVal != null)
                                    {
                                        var asTaskMethod = taskVal.GetType().GetMethod("AsTask");
                                        if (asTaskMethod != null)
                                        {
                                            var task = (Task<string>)asTaskMethod.Invoke(taskVal, null)!;
                                            source = await task;
                                        }
                                    }
                                }
                                catch {}
                            }
                        }

                        if (!string.IsNullOrEmpty(source))
                        {
                            try
                            {
                                var catalog = DeferredContractCatalog.Instance;
                                var compiled = InoCompiler.Compile(source, catalog);
                                if (compiled.Success && compiled.Plan != null)
                                {
                                    _neuronBindings = compiled.Plan.Neurons;
                                    _synapsePorts = compiled.Plan.SynapsePorts;
                                }
                            }
                            catch {}
                        }
                    }
                }
            }
        }

        if (_neuronBindings == null)
        {
            _neuronBindings = new Dictionary<string, NeuronBinding>(StringComparer.Ordinal);
        }
        if (_synapsePorts == null)
        {
            _synapsePorts = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public Task<DynamicNeuronSpec?> GetSpecAsync() => Task.FromResult(spec.Value);

    public async Task LoadAsync(DynamicNeuronSpec newSpec)
    {
        spec.Value = newSpec;
        _compiled = GetOrCompileScript(newSpec.RoslynScript);
        await WriteStateAsync();
        await ResolveBindingsAsync(CancellationToken.None);
    }

    public async Task<string> InvokeAsync(string payloadJson, string typeName, CorrelationId correlationId)
    {
        if (_compiled is null)
            throw new InvalidOperationException(
                $"DynamicNeuronGrain '{_fqn}' has no script loaded; call LoadAsync first.");

        var resolvedTypeName = typeName;
        if (_synapsePorts != null && _synapsePorts.TryGetValue(typeName, out var resolvedFqn))
        {
            resolvedTypeName = resolvedFqn;
        }

        if (Guid.TryParse(correlationId.Value, out var guidCid))
        {
            RequestContext.Set("DigitalBrain.CorrelationId", guidCid);
        }

        Console.WriteLine($"[DIAGNOSTIC-DYNAMIC] InvokeAsync called for '{_fqn}'. CorrelationId = '{correlationId.Value}', ActiveScope = '{RequestContext.Get("DigitalBrain.ActiveScope")}'");

        // Hooked directly into DigitalBrainTelemetry: Activity spans are created automatically
        using var activity = DigitalBrainTelemetry.Source.StartActivity("DynamicNeuronGrain.Invoke");
        if (activity is not null)
        {
            activity.SetTag("neuron.fqn", _fqn);
            activity.SetTag("synapse.type", resolvedTypeName);
            activity.SetTag("synapse.correlation", correlationId.Value);
        }

        var neuronHost = new DynamicNeuronHost(services, _neuronBindings ?? new Dictionary<string, NeuronBinding>(StringComparer.Ordinal));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var globals = new DynamicNeuronScriptGlobals
        {
            PayloadJson = payloadJson,
            TypeName = resolvedTypeName,
            CorrelationId = correlationId,
            Services = services,
            Neurons = neuronHost,
            CancellationToken = cts.Token,
            SynapsePorts = _synapsePorts ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Execution timeout watchdog: run with a 5-second limits
            var runTask = _compiled.RunAsync(globals: globals, cancellationToken: cts.Token);
            var completedTask = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

            if (completedTask != runTask)
            {
                cts.Cancel();
                throw new TimeoutException($"DynamicNeuronGrain '{_fqn}' script execution timed out (5s limit).");
            }

            var state = await runTask;
            var response = state.ReturnValue ?? "";

            // Telemetry: increment synapses handled counter
            DigitalBrainTelemetry.CounterInstrument(DigitalBrainTelemetry.MetricSynapsesHandled).Add(1,
                new KeyValuePair<string, object?>("neuron.fqn", _fqn),
                new KeyValuePair<string, object?>("synapse.type", typeName));

            return response;
        }
        catch (Exception ex)
        {
            // Telemetry: record errors
            DigitalBrainTelemetry.CounterInstrument(DigitalBrainTelemetry.MetricNeuronErrors).Add(1,
                new KeyValuePair<string, object?>("neuron.fqn", _fqn),
                new KeyValuePair<string, object?>("synapse.type", typeName),
                new KeyValuePair<string, object?>("error.type", ex.GetType().Name));

            throw;
        }
        finally
        {
            stopwatch.Stop();
            // Telemetry: record duration histogram
            DigitalBrainTelemetry.HistogramInstrument(DigitalBrainTelemetry.MetricHandleDurationMs).Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("neuron.fqn", _fqn),
                new KeyValuePair<string, object?>("synapse.type", typeName));
        }
    }

    // ICallNeuronTarget implementation
    public async Task<string> AskAsync(string prompt)
    {
        var cidObj = RequestContext.Get("DigitalBrain.CorrelationId");
        var cid = cidObj is Guid guid ? new CorrelationId(guid.ToString("N"))
                  : cidObj is string str ? new CorrelationId(str)
                  : CorrelationId.New();

        string payload = prompt;
        if (!prompt.TrimStart().StartsWith("{") && !prompt.TrimStart().StartsWith("["))
        {
            payload = JsonSerializer.Serialize(prompt);
        }

        return await InvokeAsync(payload, "ask", cid);
    }

    // IStreamNeuronTarget implementation
    public async IAsyncEnumerable<string> StreamAsync(string prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        var cidObj = RequestContext.Get("DigitalBrain.CorrelationId");
        var cid = cidObj is Guid guid ? new CorrelationId(guid.ToString("N"))
                  : cidObj is string str ? new CorrelationId(str)
                  : CorrelationId.New();

        string payload = prompt;
        if (!prompt.TrimStart().StartsWith("{") && !prompt.TrimStart().StartsWith("["))
        {
            payload = JsonSerializer.Serialize(prompt);
        }

        var response = await InvokeAsync(payload, "stream", cid);
        yield return response;
    }

    // IResourceNeuronTarget implementation
    public async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        var cidObj = RequestContext.Get("DigitalBrain.CorrelationId");
        var cid = cidObj is Guid guid ? new CorrelationId(guid.ToString("N"))
                  : cidObj is string str ? new CorrelationId(str)
                  : CorrelationId.New();

        var response = await InvokeAsync(JsonSerializer.Serialize(key), "read", cid);
        return response;
    }

    public async Task WriteAsync(string key, string value, CancellationToken ct)
    {
        var cidObj = RequestContext.Get("DigitalBrain.CorrelationId");
        var cid = cidObj is Guid guid ? new CorrelationId(guid.ToString("N"))
                  : cidObj is string str ? new CorrelationId(str)
                  : CorrelationId.New();

        await InvokeAsync(JsonSerializer.Serialize(new { key, value }), "write", cid);
    }

    // IPredicateNeuronTarget implementation
    public async Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        var cidObj = RequestContext.Get("DigitalBrain.CorrelationId");
        var cid = cidObj is Guid guid ? new CorrelationId(guid.ToString("N"))
                  : cidObj is string str ? new CorrelationId(str)
                  : CorrelationId.New();

        var response = await InvokeAsync(JsonSerializer.Serialize(new { subject, target }), "evaluate", cid);
        return string.Equals(response, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static object? GetServiceDynamically(IServiceProvider sp, string typeName)
    {
        var type = Type.GetType($"{typeName}, DigitalBrain.Kernel");
        if (type == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) break;
            }
        }
        return type != null ? sp.GetService(type) : null;
    }

    static Script<string> CompileScript(string source) =>
        CSharpScript.Create<string>(source, ScriptOptions.Default
            .WithReferences(
                typeof(Neuron).Assembly,
                typeof(System.Text.Json.JsonSerializer).Assembly,
                typeof(Enumerable).Assembly,
                typeof(INeuronHost).Assembly)
            .WithImports(
                "System",
                "System.Linq",
                "System.Text.Json",
                "System.Threading",
                "System.Threading.Tasks",
                "System.Collections.Generic",
                "DigitalBrain.Core",
                "Microsoft.Extensions.DependencyInjection"),
            globalsType: typeof(DynamicNeuronScriptGlobals));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Script<string>> _scriptCache =
        new(StringComparer.Ordinal);

    private static Script<string> GetOrCompileScript(string source)
    {
        return _scriptCache.GetOrAdd(source, src =>
        {
            var instrumented = InstrumentLoops(src);
            return CompileScript(instrumented);
        });
    }

    private static string InstrumentLoops(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        try
        {
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script));
            var root = tree.GetRoot();
            var rewriter = new LoopCancellationRewriter();
            var newRoot = rewriter.Visit(root);
            return newRoot.ToFullString();
        }
        catch
        {
            return source;
        }
    }

    private class LoopCancellationRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            var visitedNode = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            var checkStatement = SyntaxFactory.ParseStatement("CancellationToken.ThrowIfCancellationRequested();\r\n");
            if (visitedNode.Statement is BlockSyntax block)
            {
                return visitedNode.WithStatement(block.WithStatements(block.Statements.Insert(0, checkStatement)));
            }
            else
            {
                return visitedNode.WithStatement(SyntaxFactory.Block(checkStatement, visitedNode.Statement));
            }
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var visitedNode = (ForStatementSyntax)base.VisitForStatement(node)!;
            var checkStatement = SyntaxFactory.ParseStatement("CancellationToken.ThrowIfCancellationRequested();\r\n");
            if (visitedNode.Statement is BlockSyntax block)
            {
                return visitedNode.WithStatement(block.WithStatements(block.Statements.Insert(0, checkStatement)));
            }
            else
            {
                return visitedNode.WithStatement(SyntaxFactory.Block(checkStatement, visitedNode.Statement));
            }
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visitedNode = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
            var checkStatement = SyntaxFactory.ParseStatement("CancellationToken.ThrowIfCancellationRequested();\r\n");
            if (visitedNode.Statement is BlockSyntax block)
            {
                return visitedNode.WithStatement(block.WithStatements(block.Statements.Insert(0, checkStatement)));
            }
            else
            {
                return visitedNode.WithStatement(SyntaxFactory.Block(checkStatement, visitedNode.Statement));
            }
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            var visitedNode = (DoStatementSyntax)base.VisitDoStatement(node)!;
            var checkStatement = SyntaxFactory.ParseStatement("CancellationToken.ThrowIfCancellationRequested();\r\n");
            if (visitedNode.Statement is BlockSyntax block)
            {
                return visitedNode.WithStatement(block.WithStatements(block.Statements.Insert(0, checkStatement)));
            }
            else
            {
                return visitedNode.WithStatement(SyntaxFactory.Block(checkStatement, visitedNode.Statement));
            }
        }
    }
}

internal sealed class DynamicNeuronHost : INeuronHost
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyDictionary<string, NeuronBinding> _bindings;

    public DynamicNeuronHost(IServiceProvider services, IReadOnlyDictionary<string, NeuronBinding> bindings)
    {
        _services = services;
        _bindings = bindings;
    }

    public async Task<string> AskAsync(string port, string prompt, CancellationToken ct)
    {
        var binding = ResolveBinding(port);
        if (binding.Sigil != PortSigil.Call && binding.Sigil != PortSigil.Resource)
        {
            throw new InvalidOperationException(
                $"Neuron binding for port '{port}' has sigil {binding.Sigil}, but required Call or Resource.");
        }
        var target = _services.GetRequiredKeyedService<ICallNeuronTarget>(binding);
        return await target.AskAsync(prompt);
    }

    public async Task<bool> EvaluatePredicateAsync(string builtin, string subject, string target, CancellationToken ct)
    {
        var predicateBindings = _services.GetServices<PredicateNeuronBinding>();
        var predicateBinding = predicateBindings.FirstOrDefault(b => b.Builtin == builtin);
        if (predicateBinding != null)
        {
            var binding = new NeuronBinding(PortSigil.Predicate, predicateBinding.TargetFqn, predicateBinding.Key);
            var grain = _services.GetRequiredKeyedService<IPredicateNeuronTarget>(binding);
            return await grain.EvaluateAsync(subject, target, ct);
        }
        return false;
    }

    public IAsyncEnumerable<string> StreamAsync(string port, string prompt, CancellationToken ct)
    {
        var binding = ResolveBinding(port);
        if (binding.Sigil != PortSigil.Stream && binding.Sigil != PortSigil.Resource)
        {
            throw new InvalidOperationException(
                $"Neuron binding for port '{port}' has sigil {binding.Sigil}, but required Stream or Resource.");
        }
        var target = _services.GetRequiredKeyedService<IStreamNeuronTarget>(binding);
        return target.StreamAsync(prompt, ct);
    }

    public async Task<string?> ReadResourceAsync(string port, string key, CancellationToken ct)
    {
        var binding = ResolveBinding(port);
        EnsureSigil(binding, PortSigil.Resource, port);
        var target = _services.GetRequiredKeyedService<IResourceNeuronTarget>(binding);
        return await target.ReadAsync(key, ct);
    }

    public async Task WriteResourceAsync(string port, string key, string value, CancellationToken ct)
    {
        var binding = ResolveBinding(port);
        EnsureSigil(binding, PortSigil.Resource, port);
        var target = _services.GetRequiredKeyedService<IResourceNeuronTarget>(binding);
        await target.WriteAsync(key, value, ct);
    }

    private NeuronBinding ResolveBinding(string port)
    {
        if (_bindings.TryGetValue(port, out var binding))
            return binding;
        throw new InvalidOperationException(
            $"No neuron binding for port '{port}'.");
    }

    private static void EnsureSigil(NeuronBinding binding, PortSigil expected, string port)
    {
        if (binding.Sigil == expected)
            return;
        throw new InvalidOperationException(
            $"Neuron binding for port '{port}' has sigil {binding.Sigil}, but required {expected}.");
    }
}

// Lowers InoLang AST ExecutionPlan to C# Roslyn Script statements
public static class InoToScriptTranspiler
{
    public static string Transpile(ExecutionPlan plan)
    {
        var sb = new StringBuilder();
        foreach (var handler in plan.AllHandlers)
        {
            var triggerFqn = handler.Key.Key;
            if (handler.Key.Category == TriggerCategory.Port && plan.SynapsePorts.TryGetValue(triggerFqn, out var fqn))
            {
                triggerFqn = fqn;
            }
            sb.AppendLine($"if (TypeName == \"{triggerFqn}\")");
            sb.AppendLine("{");
            sb.AppendLine("    var vars = new Dictionary<string, string>(StringComparer.Ordinal);");
            foreach (var stmt in handler.Body)
            {
                TranspileStmt(stmt, sb, 1);
            }
            sb.AppendLine("    return vars.Count > 0 ? vars.Values.Last() : \"\";");
            sb.AppendLine("}");
        }
        sb.AppendLine("return \"\";");
        return sb.ToString();
    }

    private static void TranspileStmt(Stmt stmt, StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent * 4);
        switch (stmt)
        {
            case LetAskStmt l:
                sb.AppendLine($"{pad}vars[\"{l.Var}\"] = await AskAsync(\"{l.Port}\", {FormatExpr(l.Prompt)});");
                break;
            case LetExprStmt le:
                sb.AppendLine($"{pad}vars[\"{le.Var}\"] = {FormatExpr(le.Value)};");
                break;
            case EmitStmt e:
                var args = string.Join(", ", e.Args.Select(a => $"[\"{a.Name}\"] = {FormatExpr(a.Value)}"));
                sb.AppendLine($"{pad}await EmitAsync(\"{e.Port}\", new Dictionary<string, string> {{ {args} }});");
                break;
            case SaveStmt s:
                sb.AppendLine($"{pad}await WriteStateAsync(\"{s.Port}\", {FormatExpr(s.Value)});");
                break;
            case RememberStmt r:
                sb.AppendLine($"{pad}await WriteStateAsync({FormatExpr(r.Text)}, {FormatExpr(r.Value ?? new StringExpr("", r.Span))});");
                break;
            case WriteStmt w:
                sb.AppendLine($"{pad}await WriteStateAsync({FormatExpr(w.Target)}, {FormatExpr(w.Value)});");
                break;
            case LogStmt lg:
                sb.AppendLine($"{pad}// Log: {FormatExpr(lg.Message)}");
                break;
            case IfStmt i:
                sb.AppendLine($"{pad}if (!string.IsNullOrEmpty({FormatExpr(i.Cond)}) && !string.Equals({FormatExpr(i.Cond)}, \"false\", StringComparison.OrdinalIgnoreCase))");
                sb.AppendLine($"{pad}{{");
                foreach (var s in i.ThenBody) TranspileStmt(s, sb, indent + 1);
                sb.AppendLine($"{pad}}}");
                if (i.ElseBody.Count > 0)
                {
                    sb.AppendLine($"{pad}else");
                    sb.AppendLine($"{pad}{{");
                    foreach (var s in i.ElseBody) TranspileStmt(s, sb, indent + 1);
                    sb.AppendLine($"{pad}}}");
                }
                break;
        }
    }

    private static string FormatExpr(Expr expr)
    {
        return expr switch
        {
            StringExpr s => $"\"{s.Value.Replace("\"", "\\\"")}\"",
            NumberExpr n => $"\"{n.Value}\"",
            PortRefExpr p => $"vars.GetValueOrDefault(\"{p.Name}\", \"{p.Name}\")",
            FieldAccessExpr f => $"GetField(\"{f.Field}\")",
            CallExpr c => $"FormatCallExpr(\"{c.Builtin}\", {FormatExpr(c.Arg)})",
            ArgsExpr a => string.Join(" + \",\" + ", a.Args.Select(arg => $"\"{arg.Name}:\" + {FormatExpr(arg.Value)}")),
            InterpExpr interp => string.Join(" + ", interp.Parts.Select(FormatExpr)),
            _ => "\"\""
        };
    }
}
