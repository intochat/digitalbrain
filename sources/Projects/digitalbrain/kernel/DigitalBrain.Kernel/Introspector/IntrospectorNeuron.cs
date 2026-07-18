using System.Text.Json;
using DigitalBrain.Runtime.Introspector;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Kernel.Conversation;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.User;
using DigitalBrain.Kernel.Visualization;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Testing;
using DigitalBrain.Kernel.Creator.InoAuthoring;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.InoLang.Linking;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.History;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Catalog;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.Explaining;
namespace DigitalBrain.Kernel.Introspector;

[global::Orleans.GrainType("DigitalBrain.Kernel.Introspector.IntrospectorNeuron")]
[global::Orleans.ImplicitStreamSubscription(nameof(IntrospectorNeuron))]
public sealed class IntrospectorNeuron(
    [FromKeyedServices("incoming")]          IDurableList<Synapse>            incoming,
    [FromKeyedServices("outgoing")]          IDurableList<Synapse>            outgoing,
    [FromKeyedServices("outstanding-explain")] IDurableList<OutstandingExplain> outstandingExplain,
    IGrainFactory grains,
    INeuronFeatureLoader featureLoader,
    HomeFeedBus homeFeed,
    IContractCatalog catalog,
    TimeProvider time,
    DynamicDomainRegistry dbRegistry,
    IInterpretedNeuronRegistry interpretedRegistry,
    InoAuthoringLoop authoringLoop,
    ILogger<IntrospectorNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      IIntrospector, INeuronMetadata,
      IHandle<FindNeuronsByFeatureTextRequest>,
      IHandle<FindChainsByConversationTextRequest>,
      IHandle<TraceCorrelationRequest>,
      IHandle<GetRecentActivityRequest>,
      IHandle<FindRootSynapseRequest>,
      IHandle<ExplainDecisionRequest>,
      IHandle<ExplainerResponse>,
      IHandle<QueryCatalogContractsRequest>,
      IHandle<VerifyBddScenariosRequest>,
      IHandle<RegisterCatalogContractRequest>,
      IHandle<PromoteNeuronRequest>,
      IHandle<AutoGenerateNeuronRequest>,
      IHandle<RollbackNeuronRequest>
{
    public static NeuronId         Id           => new("kernel/introspector");
    public static string           Icon         => "introspector";
    public static NeuronCapability Capabilities => NeuronCapability.None;

    public async Task<IReadOnlyList<NeuronRef>> FindNeuronsByFeatureTextAsync(
        string query, int limit, CancellationToken ct)
    {
        var catalog = Grains.GetGrain<IBrainCatalog>("global");
        var registered = await catalog.ListRegisteredAsync();
        var normalizedQuery = query.ToLowerInvariant();
        var results = new List<NeuronRef>();

        foreach (var entry in registered)
        {
            if (results.Count >= limit) break;

            var feature = featureLoader.GetFeature(entry.TypeFullName);
            if (feature is null) continue;

            if (!feature.Value.Text.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) continue;

            // Extract a short snippet around the first match.
            var text = feature.Value.Text;
            var idx = text.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            var snippetStart = Math.Max(0, idx - 40);
            var snippetEnd = Math.Min(text.Length, idx + query.Length + 80);
            var snippet = text[snippetStart..snippetEnd].Trim();

            results.Add(new NeuronRef(entry.TypeFullName, entry.Domain, snippet));
        }

        return results;
    }

    public async Task<IReadOnlyList<Guid>> FindChainsByConversationTextAsync(
        string text, DateTimeOffset? since, DateTimeOffset? until, int limit, CancellationToken ct)
    {
        var conv = Grains.GetGrain<IConversation>("default");
        var hits = await conv.SearchAsync(text, since, until, limit, ct);
        return hits.Select(m => m.CorrelationId).Distinct().ToArray();
    }

    public async Task<IReadOnlyList<Synapse>> TraceCorrelationAsync(Guid correlationId, CancellationToken ct)
    {
        var chain = Grains.GetGrain<ICorrelationChain>(correlationId);
        var snapshot = await chain.SnapshotAsync(ct);
        return snapshot.OrderBy(s => s.Timestamp).ToArray();
    }

    public Task<IReadOnlyList<Guid>> GetRecentActivityAsync(string userId, TimeSpan since, CancellationToken ct)
    {
        var user = Grains.GetGrain<IUserNeuron>(userId);
        return user.GetRecentCorrelationIdsAsync(since, ct);
    }

    public async Task<Synapse?> FindRootSynapseAsync(Guid synapseId, CancellationToken ct)
    {
        // Scan the in-memory recent-window buffer from BrainTimelineRelayGrain.
        // This covers the last 500 synapses (Mission C answer). Deep history walk
        // across grain storage is a Mission D concern.
        var relay = Grains.GetGrain<IBrainTimelineRelay>(Guid.Empty);
        var recent = await relay.SnapshotAsync(default);

        // Build a fast lookup by SynapseId.
        var byId = recent.ToDictionary(s => s.SynapseId);

        if (!byId.TryGetValue(synapseId, out var current))
            return null;

        // Walk causation chain toward the root (a synapse with no parent in the window).
        while (current.CausationId is { } parentId && byId.TryGetValue(parentId, out var parent))
            current = parent;

        return current;
    }

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        switch (s)
        {
            case FindNeuronsByFeatureTextRequest req:
            {
                var neurons = await FindNeuronsByFeatureTextAsync(req.Query, req.Limit, default);
                await FireSynapseAsync(new FindNeuronsByFeatureTextResponse(Neurons:            neurons) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });
                break;
            }
            case FindChainsByConversationTextRequest req:
            {
                var ids = await FindChainsByConversationTextAsync(req.Text, req.Since, req.Until, req.Limit, default);
                await FireSynapseAsync(new FindChainsByConversationTextResponse(CorrelationIds:     ids) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });
                break;
            }
            case TraceCorrelationRequest req:
            {
                var chain = await TraceCorrelationAsync(req.TargetCorrelationId, default);
                await FireSynapseAsync(new TraceCorrelationResponse(Chain:              chain) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });
                break;
            }
            case GetRecentActivityRequest req:
            {
                var ids = await GetRecentActivityAsync(req.UserId, req.Since, default);
                await FireSynapseAsync(new GetRecentActivityResponse(CorrelationIds:     ids) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });
                break;
            }
            case FindRootSynapseRequest req:
            {
                var root = await FindRootSynapseAsync(req.SynapseIdToTrace, default);
                await FireSynapseAsync(new FindRootSynapseResponse(Root:               root) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });
                break;
            }
            case ExplainDecisionRequest req:
            {
                outstandingExplain.Add(new OutstandingExplain(
                    CorrelationId:             req.CorrelationId,
                    OriginalCallerNeuronId:    req.CallerNeuronId,
                    OriginalCallerNeuronType:  req.CallerNeuronType,
                    OriginalRequestSynapseId:  req.SynapseId
                ));
                await WriteStateAsync();
                await FireSynapseAsync(new ExplainerRequest(NaturalLanguageQuery: req.NaturalLanguageQuery,
        UserId:               req.UserId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: AiNeuronTypes.ExplainerNeuron,
            timestamp: time.GetUtcNow()
        ) });
                break;
            }
            case ExplainerResponse exp:
            {
                var idx = outstandingExplain.ToList().FindIndex(o => o.CorrelationId == exp.CorrelationId);
                if (idx < 0) break;
                var open = outstandingExplain[idx];
                await FireSynapseAsync(new ExplainDecisionResponse(NaturalLanguageAnswer: exp.NaturalLanguageAnswer,
        CitedCorrelationIds:   exp.CitedCorrelationIds) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: exp.CorrelationId,
            causationId: exp.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: open.OriginalCallerNeuronId,
            receiverNeuronType: open.OriginalCallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });

                var cardDataJson = JsonSerializer.Serialize(new
                {
                    answer      = exp.NaturalLanguageAnswer,
                    citedChains = exp.CitedCorrelationIds.Select(g => g.ToString()).ToArray(),
                });
                await homeFeed.BroadcastAsync(new RfwCard(LibraryName:        "digitalbrain",
        RootWidget:         "ExplainAnswerCard",
        DataJson:           cardDataJson) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: exp.CorrelationId,
            causationId: exp.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: open.OriginalCallerNeuronId,
            receiverNeuronType: open.OriginalCallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) },
                    CancellationToken.None);

                outstandingExplain.RemoveAt(idx);
                await WriteStateAsync();
                break;
            }
            case QueryCatalogContractsRequest req:
            {
                var internalSchemas = catalog.GetAllSchemas();
                var catalogSchemas = internalSchemas.Select(s => new CatalogContractSchema(
                    s.Fqn,
                    s.Kind switch
                    {
                        ContractKind.Synapse => CatalogContractKind.Synapse,
                        _                    => CatalogContractKind.Neuron
                    },
                    s.Fields)).ToArray();

                await FireSynapseAsync(new QueryCatalogContractsResponse(Schemas:            catalogSchemas) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });
                break;
            }
            case VerifyBddScenariosRequest verifyReq:
            {
                await HandleAsync(verifyReq, default);
                break;
            }
            case RegisterCatalogContractRequest regReq:
            {
                await HandleAsync(regReq, default);
                break;
            }
            case PromoteNeuronRequest promoteReq:
            {
                await HandleAsync(promoteReq, default);
                break;
            }
            case AutoGenerateNeuronRequest genReq:
            {
                await HandleAsync(genReq, default);
                break;
            }
            case RollbackNeuronRequest rollbackReq:
            {
                await HandleAsync(rollbackReq, default);
                break;
            }
        }
    }

    public async Task HandleAsync(VerifyBddScenariosRequest req, CancellationToken ct)
    {
        var compiled = InoCompiler.Compile(req.InoSource, catalog);
        bool passed = false;
        string diagnosticsJson = "[]";
        string scenariosJson = "[]";

        if (!compiled.Success)
        {
            var errors = compiled.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Code} {d.Message}")
                .ToArray();
            diagnosticsJson = JsonSerializer.Serialize(errors);
        }
        else if (compiled.Plan!.Scenarios.Count == 0)
        {
            diagnosticsJson = JsonSerializer.Serialize(new[] { "BOSN002: L6 Gate Violation - Document contains zero scenarios. Every neuron must carry at least one scenario block." });
        }
        else
        {
            var scenarioRunner = new ScenarioRunner();
            var report = await scenarioRunner.RunAllAsync(compiled.Plan, ct);
            passed = report.AllPassed;
            
            var results = report.Results.Select(r => new {
                name = r.Name,
                passed = r.Passed,
                failures = r.Failures
            }).ToArray();
            scenariosJson = JsonSerializer.Serialize(results);
            
            var errors = compiled.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Code} {d.Message}")
                .ToArray();
            diagnosticsJson = JsonSerializer.Serialize(errors);
        }

        await FireSynapseAsync(new VerifyBddScenariosResponse(Passed: passed,
            DiagnosticsJson: diagnosticsJson,
            ScenariosJson: scenariosJson) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: req.CorrelationId,
                causationId: req.SynapseId,
                callerNeuronId: InstanceId,
                callerNeuronType: nameof(IntrospectorNeuron),
                receiverNeuronId: req.CallerNeuronId,
                receiverNeuronType: req.CallerNeuronType ?? "External",
                timestamp: time.GetUtcNow()
            ) }, ct);
    }

    public async Task HandleAsync(RegisterCatalogContractRequest req, CancellationToken ct)
    {
        bool success = false;
        string message = "";
        try
        {
            var kind = req.Kind switch
            {
                CatalogContractKind.Synapse => ContractKind.Synapse,
                _ => ContractKind.Neuron
            };
            catalog.Register(new ContractSchema(req.Fqn, kind, req.Fields));
            success = true;
            message = $"Registered contract '{req.Fqn}' successfully.";
        }
        catch (Exception ex)
        {
            message = $"Failed to register contract: {ex.Message}";
        }

        await FireSynapseAsync(new RegisterCatalogContractResponse(Success: success,
            Message: message) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: req.CorrelationId,
                causationId: req.SynapseId,
                callerNeuronId: InstanceId,
                callerNeuronType: nameof(IntrospectorNeuron),
                receiverNeuronId: req.CallerNeuronId,
                receiverNeuronType: req.CallerNeuronType ?? "External",
                timestamp: time.GetUtcNow()
            ) }, ct);
    }

    public async Task HandleAsync(PromoteNeuronRequest req, CancellationToken ct)
    {
        bool success = false;
        string version = "1.0.0";
        string message = "";

        try
        {
            var compiled = InoCompiler.Compile(req.InoSource, catalog);
            if (!compiled.Success || compiled.Linked is null)
            {
                var errors = string.Join(" | ", compiled.Diagnostics.Select(d => d.Code + " " + d.Message));
                throw new InvalidOperationException($"Neuron compilation failed: {errors}");
            }

            var registration = LinkedPortCatalogContributor.BuildRegistration(req.InoSource, compiled.Linked);

            // Persist to database
            int nextVer = await dbRegistry.SaveNeuronAsync(req.Fqn, req.InoSource, ct);
            version = $"1.0.{nextVer}";

            // Register dynamically
            await interpretedRegistry.RegisterDynamicAsync(registration);

            // Proactively hot-swap the active DynamicNeuronGrain in memory
            var scriptSource = InoToScriptTranspiler.Transpile(compiled.Plan!);
            var newSpec = new DynamicNeuronSpec(
                Id: new NeuronId(req.Fqn),
                FeatureText: "",
                RoslynScript: scriptSource,
                CreatedAt: DateTimeOffset.UtcNow,
                Status: DynamicNeuronStatus.Promoted
            );
            var grain = Grains.GetGrain<IDynamicNeuron>(req.Fqn);
            await grain.LoadAsync(newSpec);

            success = true;
            message = $"Neuron '{req.Fqn}' promoted and hot-swapped successfully to version {version}.";
        }
        catch (Exception ex)
        {
            message = $"Failed to promote neuron: {ex.Message}";
        }

        await FireSynapseAsync(new PromoteNeuronResponse(Success: success,
            Version: version,
            Message: message) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: req.CorrelationId,
                causationId: req.SynapseId,
                callerNeuronId: InstanceId,
                callerNeuronType: nameof(IntrospectorNeuron),
                receiverNeuronId: req.CallerNeuronId,
                receiverNeuronType: req.CallerNeuronType ?? "External",
                timestamp: time.GetUtcNow()
            ) }, ct);
    }

    public async Task HandleAsync(AutoGenerateNeuronRequest req, CancellationToken ct)
    {
        bool success = false;
        string inoSource = "";
        string error = "";

        try
        {
            var authoringRequest = new InoAuthoringRequest(
                Intent: req.Intent,
                SuggestedFqn: req.SuggestedFqn,
                LlmModelKey: req.LlmModelKey,
                MaxAttempts: 3
            );

            var result = await authoringLoop.AuthorAsync(authoringRequest, null, ct);
            inoSource = result.LastInoSource ?? "";
            error = result.FinalError ?? "";

            if (result.Green)
            {
                var registration = result.Registration
                    ?? throw new InvalidOperationException(
                        "A green .ino authoring result did not include a runtime registration.");
                await interpretedRegistry.RegisterDynamicAsync(registration);
                success = true;
            }
        }
        catch (Exception ex)
        {
            error = $"Authoring loop threw exception: {ex.Message}";
        }

        await FireSynapseAsync(new AutoGenerateNeuronResponse(Success: success,
            InoSource: inoSource,
            Error: error) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: req.CorrelationId,
                causationId: req.SynapseId,
                callerNeuronId: InstanceId,
                callerNeuronType: nameof(IntrospectorNeuron),
                receiverNeuronId: req.CallerNeuronId,
                receiverNeuronType: req.CallerNeuronType ?? "External",
                timestamp: time.GetUtcNow()
            ) }, ct);
    }

    public async Task HandleAsync(RollbackNeuronRequest req, CancellationToken ct)
    {
        bool success = false;
        string message = "";

        try
        {
            var versionRecord = await dbRegistry.GetVersionAsync(req.Fqn, req.TargetVersion, ct);
            if (versionRecord == null)
            {
                throw new InvalidOperationException($"Dynamic neuron '{req.Fqn}' version {req.TargetVersion} was not found.");
            }

            var compiled = InoCompiler.Compile(versionRecord.SourceCode, catalog);
            if (!compiled.Success || compiled.Linked is null)
            {
                var errors = string.Join(" | ", compiled.Diagnostics.Select(d => d.Code + " " + d.Message));
                throw new InvalidOperationException($"Historical neuron compilation failed: {errors}");
            }

            var registration = LinkedPortCatalogContributor.BuildRegistration(
                versionRecord.SourceCode, compiled.Linked);

            // Revert in SQLite
            await dbRegistry.RollbackNeuronAsync(req.Fqn, req.TargetVersion, ct);

            // Register dynamically
            await interpretedRegistry.RegisterDynamicAsync(registration);

            // Proactively hot-swap the active DynamicNeuronGrain in memory
            var scriptSource = InoToScriptTranspiler.Transpile(compiled.Plan!);
            var newSpec = new DynamicNeuronSpec(
                Id: new NeuronId(req.Fqn),
                FeatureText: "",
                RoslynScript: scriptSource,
                CreatedAt: DateTimeOffset.UtcNow,
                Status: DynamicNeuronStatus.Promoted
            );
            var grain = Grains.GetGrain<IDynamicNeuron>(req.Fqn);
            await grain.LoadAsync(newSpec);

            success = true;
            message = $"Neuron '{req.Fqn}' successfully rolled back and hot-swapped to version {req.TargetVersion}.";
        }
        catch (Exception ex)
        {
            message = $"Failed to rollback neuron: {ex.Message}";
        }

        await FireSynapseAsync(new RollbackNeuronResponse(Success: success,
            Message: message) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: req.CorrelationId,
                causationId: req.SynapseId,
                callerNeuronId: InstanceId,
                callerNeuronType: nameof(IntrospectorNeuron),
                receiverNeuronId: req.CallerNeuronId,
                receiverNeuronType: req.CallerNeuronType ?? "External",
                timestamp: time.GetUtcNow()
            ) }, ct);
    }
}
