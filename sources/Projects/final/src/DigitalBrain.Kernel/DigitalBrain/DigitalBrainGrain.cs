using DigitalBrain.Awesome;
using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.State;
using DigitalBrain.Os.UI;
using DigitalBrain.Hosting.DigitalBrain;
using DigitalBrain.Hosting.Microsoft.Aspire;
using DigitalBrain.Protocol.Microsoft.Aspire;
using DigitalBrain.Hosting.Microsoft.Flutter;
using Microsoft.Extensions.DependencyInjection;
// T2 connectors: removed using DigitalBrain.Hosting.Microsoft.Windows (FileSystem moved to Connectors/Experiences/FileSystemConnectorGrain; GrainType "filesystem" now provides it. Activation here via INeuron(key) + GrainType registration (no concrete type needed). Scoped change only.
using Orleans;
using Orleans.Providers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain")]
[StorageProvider(ProviderName = "Default")]
public sealed class DigitalBrainGrain : Neuron, IDigitalBrain, ICluster, IHandle<BundleInstalled>, IHandle<StartQuarantineWorld>, IHandle<UpdateBundle>, IHandle<DigitalBrain.Os.Domain.Events.ForkBrain>, IHandle<UninstallBundle>, IHandle<RunExperience>
{
    private readonly IPersistentState<NeuronState> _profile;

    public DigitalBrainGrain(
        [PersistentState(NeuronStateKeys.State, "Default")] IPersistentState<NeuronState> profile)
    {
        _profile = profile;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        // Auto-trigger the gmail-senders-chart RunExperience on root brain activate so that
        // dotnet run ino.cs produces the BarChart surface immediately. This allows dart mcp
        // get_widget_tree on the flutter client to see the live BarChart UI for the experience.
        if (string.Equals(this.GetPrimaryKeyString(), "root", StringComparison.OrdinalIgnoreCase))
        {
            _ = SendAsync(new RunExperience((ExperienceId)"gmail-senders-chart"), cancellationToken);
        }
    }

    public async Task SendAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        if (synapse is InstallFromMarketplace installFromMarketplaceSynapse)
        {
            // Task1 reliability fix: route InstallFromMarketplace via guaranteed (p2p/awaited) direct grain-to-grain
            // instead of sole reliance on lossy DigitalBrainTimeline memory stream broadcast (late sub miss).
            // Ref (caller verifs + web): direct GrainFactory.GetGrain<IMarketplace>.HandleAsync is reliable delivery
            // (Orleans grain call semantics) vs memory stream replay issue for not-yet-active subscriptions.
            var domainKey = this.GetPrimaryKeyString();
            var guaranteedMarketplaceGrain = GrainFactory.GetGrain<IMarketplace>(domainKey);
            // Fire the direct call (delivery starts immediately in runtime); do not await in this turn to avoid
            // reentrancy deadlock (mkt.Handle does callback to this brain's InstallBundleAsync during the Send).
            // The p2p/awaited happens inside the started delivery; emit for observers remains in caller turn.
            _ = guaranteedMarketplaceGrain.HandleAsync(installFromMarketplaceSynapse, cancellationToken);
        }
        await Emit(synapse);
    }

    public Task<IReadOnlyList<NeuronId>> ListSubscribersAsync(string synapseTypeName, CancellationToken cancellationToken = default)
    {
        var s = _profile.State;
        int staticForType = SynapseDispatch.GetStaticHandlerCountFor(synapseTypeName);
        int dynamicInstalled = (synapseTypeName == nameof(BundleInstalled) || synapseTypeName == "InstallBundle") ? s.InstalledBundles.Count : 0;

        // Contract contributions (private/contract-only marketplace): +1 per installed contract bundle that declares an IHandle for the synapse type.
        // Enables N+1 growth observable via ListSubscribers even when only the shape (not impl) was distributed.
        int contractContributions = 0;
        if (s.ContractBundles != null && s.ContractBundles.Count > 0)
        {
            var suffix = "." + synapseTypeName;
            foreach (var decls in s.ContractBundles.Values)
            {
                if (decls != null && decls.Any(d => d.IsHandle && (d.SynapseType == synapseTypeName || d.SynapseType.EndsWith(suffix))))
                    contractContributions++;
            }
        }

        // count = 1 (brain) + installed (dynamic per bundle for system events) + staticForType (source-gen KnownContracts) + contract decl contributions
        int count = 1 + dynamicInstalled + staticForType + contractContributions;
        var list = new List<NeuronId>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(NeuronId.For(typeof(IDigitalBrain).FullName!, i.ToString()));
        }
        return Task.FromResult<IReadOnlyList<NeuronId>>(list);
    }

    public Task<IReadOnlyList<string>> ListActiveNeuronTypesAsync(CancellationToken cancellationToken = default)
    {
        var s = _profile.State;
        var types = new List<string> { typeof(IDigitalBrain).FullName! };
        types.AddRange(s.InstalledBundles.Select(e => $"bundle-{e}"));
        if (s.ContractBundles != null)
            types.AddRange(s.ContractBundles.Keys.Select(e => $"contract-{e}"));
        return Task.FromResult<IReadOnlyList<string>>(types);
    }

    public async Task<BundleInstalled> InstallBundleAsync(InstallBundle command, CancellationToken cancellationToken = default)
    {
        // Source-prefixed bundle prep (ino:/github:/nuget: etc) for automatic creation at runtime: triggers resolve intent telemetry + optional target domain.
        // Install (via bundle) to domain-keyed brain grows N+1 on system events with no restart. Journals for replay.
        var id = command.BundleId.Value;
        string? targetDomain = command.TargetDomainId;
        if (!string.IsNullOrWhiteSpace(targetDomain))
        {
            await Emit(new NeuronTelemetry(Self, "InstallTargetedToDomain", new Dictionary<string, string> { ["bundle"] = id, ["domain"] = targetDomain }));
        }

        string effectiveId = id;
        if (id.Contains(':') && (id.StartsWith("ino:") || id.StartsWith("nuget:") || id.StartsWith("github:")))
        {
            var parts = id.Split(':', 2);
            await Emit(new NeuronTelemetry(Self, "BundleSourceResolveIntent", new Dictionary<string, string> { ["source"] = parts[0], ["id"] = id, ["targetDomain"] = targetDomain ?? "global" }));
            effectiveId = parts.Length > 1 ? parts[1] : id;
        }

        var toInstallId = (BundleId)effectiveId;

        var s = _profile.State;

        if (command.IsContractOnly)
        {
            // Contract-only: track via ContractBundles (for decl-driven subscriber counts + distinct active "contract-*" listing).
            // Do not add to InstalledBundles (keeps "bundle-*" only for full experiences that activate impls).
        }
        else
        {
            if (!s.InstalledBundles.Contains(toInstallId))
                s.InstalledBundles.Add(toInstallId);
        }

        // Store decls for contrib math (contract or rule bundles that declare handlers; rule uses same shape and count logic).
        if (command.ContractHandlers is { Length: > 0 })
        {
            s.ContractBundles[toInstallId] = command.ContractHandlers;
        }

        if (!string.IsNullOrWhiteSpace(targetDomain))
            s.CustomState[$"last-install-domain:{toInstallId}"] = targetDomain;

        await ResolveBundleSourceContentAsync(command, toInstallId, id, targetDomain, cancellationToken);

        if (command.HasRules)
        {
            var ruleHost = GrainFactory.GetGrain<IRuleHostNeuron>(this.GetPrimaryKeyString());
            DigitalBrain.InoLang.Domain.Ino.InoExperience? full = null;
            var contentKey = $"bundle-content:{toInstallId}";
            if (s.CustomState.TryGetValue(contentKey, out var contentText) && !string.IsNullOrWhiteSpace(contentText))
            {
                // Dual yaml/.ino per OS-ON-YAML-SPEC: yaml content (from experience.yaml) maps via YamlParser to same InoExperience.
                try
                {
                    full = contentText.Contains("schemaVersion: \"os-on-yaml/", StringComparison.OrdinalIgnoreCase)
                        ? DigitalBrain.InoLang.Domain.Yaml.YamlParser.Parse(contentText)
                        : DigitalBrain.InoLang.Domain.Ino.InoParser.Parse(contentText);
                }
                catch { }
            }
            DigitalBrain.InoLang.Domain.Ino.RuleSet rs;
            if (full != null)
            {
                rs = new DigitalBrain.InoLang.Domain.Ino.RuleSet(full.Rules, full.Emits ?? Array.Empty<string>());
                if (full.IsSystem)
                    s.CustomState[$"system:{toInstallId}"] = "true";
            }
            else
            {
                var decls = (command.ContractHandlers ?? Array.Empty<ContractDeclaration>())
                    .Where(d => d.IsHandle)
                    .Select(d => new DigitalBrain.InoLang.Domain.Ino.RuleDeclaration(d.SynapseType, null, null, Array.Empty<DigitalBrain.InoLang.Domain.Ino.RuleStatement>()))
                    .ToArray();
                rs = new DigitalBrain.InoLang.Domain.Ino.RuleSet(decls, Array.Empty<string>());
            }
            await ruleHost.InstallRulesAsync(toInstallId.Value, rs);
        }

        await _profile.WriteStateAsync(cancellationToken);

        if (!command.IsContractOnly)
        {
            await DigitalBrainLauncher.ActivateExperiencesFor(GrainFactory, toInstallId);
        }

        await Emit(command);
        var installed = new BundleInstalled(toInstallId);
        await Emit(installed);

        return installed;
    }

    public async Task HandleAsync(BundleInstalled synapse, CancellationToken cancellationToken)
    {
        await Emit(new HandlerReacted(synapse.BundleId, "core-brain"));
    }

    public async Task HandleAsync(ClientTap tap, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tap.SynapseJson))
            return;

        // Deliver the tapped synapse into the brain (the real OnTap from the Flutter UI button).
        // This is the proper back-channel: tapping in Flutter has the same effect as tapping in hex1b.
        try
        {
            using var doc = JsonDocument.Parse(tap.SynapseJson);
            var root = doc.RootElement;

            string? type = null;
            if (root.TryGetProperty("Type", out var t1)) type = t1.GetString();
            else if (root.TryGetProperty("type", out var t2)) type = t2.GetString();
            else if (root.TryGetProperty("$type", out var t3)) type = t3.GetString();

            type ??= string.Empty;

            if (type.Contains("telemetry", StringComparison.OrdinalIgnoreCase))
            {
                var evt = root.TryGetProperty("Event", out var e) ? e.GetString() : "UiEvent";
                var data = new Dictionary<string, string>();
                if (root.TryGetProperty("Data", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in d.EnumerateObject())
                    {
                        data[prop.Name] = prop.Value.ToString();
                    }
                }

                // UiNeuron remnant deleted per C Task 2 (single source UI). Telemetry already emitted below as NeuronTelemetry (observed by Shell workspace + timeline). No more GetGrain<IUiNeuron> for the uineuron grain type.
                await Emit(new NeuronTelemetry(Self, evt ?? "UiEvent", data));
                return;
            }

            if (type.Contains("SwitchBrain", StringComparison.OrdinalIgnoreCase))
            {
                var brainId = root.TryGetProperty("BrainId", out var b) ? b.GetString() : string.Empty;
                var username = root.TryGetProperty("Username", out var u) ? u.GetString() : string.Empty;
                if (!string.IsNullOrWhiteSpace(brainId) && !string.IsNullOrWhiteSpace(username))
                {
                    // UiNeuron deleted per C; Shell (implements IUiNeuron compat) now owns brain switch via its own telemetry registration. No direct GetGrain<IUiNeuron> remnant route.
                    // The ClientTap for switch falls through to telemetry emit for observability.
                }
            }

            if (type.Contains("AddBrain", StringComparison.OrdinalIgnoreCase))
            {
                var brainId = root.TryGetProperty("BrainId", out var b) ? b.GetString() : string.Empty;
                var username = root.TryGetProperty("Username", out var u) ? u.GetString() : string.Empty;
                if (!string.IsNullOrWhiteSpace(brainId) && !string.IsNullOrWhiteSpace(username))
                {
                    // UiNeuron deleted per C; AddBrain now no-op here (research path), telemetry will be emitted below. Shell workspace handles via its RegisterTelemetry.
                }
            }

            if (type.Contains("RunDistributionSimulation", StringComparison.OrdinalIgnoreCase))
            {
                // Marketplace surface "Run distribution simulation" button tapped in Flutter: deliver through the
                // normal path so MarketplaceNeuron's IHandle runs the real publish + install loop (same as hex1b tap).
                await SendAsync(new RunDistributionSimulation(), cancellationToken);
                return;
            }

            if (type.Contains("Demo", StringComparison.OrdinalIgnoreCase))
            {
                // Headless + browser DEMO support: any ClientTap with Type containing "Demo" causes a real
                // server-emitted UiSurface. This makes "press DEMO" (or mcp/resource cmd send) produce
                // observable live surface on the timeline + delivered to all connected clients (flutter, tui, etc.).
                var now = DateTime.UtcNow.ToString("HH:mm:ss");
                await Emit(new UiSurface(
                    "demo-executed",
                    Self,
                    new Card("Demo Executed", new Column(new UiWidget[]
                    {
                        new Text($"Time: {now}"),
                        new Text("Triggered via ClientTap (browser DEMO or headless/mcp)"),
                    }))
                ));
                await Emit(new NeuronTelemetry(Self, "DemoExecuted", new Dictionary<string, string> { ["source"] = "ClientTap" }));
                Console.WriteLine("[DEMO] server emitted UiSurface(demo-executed) + DemoExecuted telemetry");
                return;
            }

            if (type.Contains("InstallFromMarketplace", StringComparison.OrdinalIgnoreCase))
            {
                // Route via brain.SendAsync(InstallFromMarketplace) so the guaranteed direct delivery (in SendAsync)
                // + emit for observers is used (ClientTap backchannel now uses same reliable path as hex1b Send for E reliability).
                var id = root.TryGetProperty("ExperienceId", out var e) ? e.GetString()
                       : root.TryGetProperty("BundleId", out var b) ? b.GetString()
                       : root.TryGetProperty("Id", out var i) ? i.GetString()
                       : string.Empty;
                var peerAddr = root.TryGetProperty("PeerAddress", out var p) ? p.GetString()
                             : root.TryGetProperty("peerAddress", out var p2) ? p2.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    await SendAsync(new InstallFromMarketplace((ExperienceId)id, peerAddr), cancellationToken);
                    return;
                }
            }

            if (type.Contains("PinSurface", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit reconstruction for shell nav clicks (e.g. sidebar "🛒 Marketplace" PinSurface).
                // Binder path was not reliably producing a PinSurface that ShellNeuron IHandle<PinSurface> would process
                // (leading to no placement, no workspace/ui-shell re-emit, no view/window appearing).
                // Now constructs concrete and SendAsync (emits after any special routing).
                var surfaceId = root.TryGetProperty("SurfaceId", out var s) ? s.GetString() : string.Empty;
                var region = root.TryGetProperty("Region", out var r) ? r.GetString() ?? "main" : "main";
                var order = root.TryGetProperty("Order", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
                if (!string.IsNullOrWhiteSpace(surfaceId))
                {
                    await SendAsync(new PinSurface(surfaceId, region, order), cancellationToken);
                    return;
                }
            }

            if (type.Contains("OpenWindow", StringComparison.OrdinalIgnoreCase))
            {
                var surfaceId = root.TryGetProperty("SurfaceId", out var s) ? s.GetString() : string.Empty;
                var title = root.TryGetProperty("Title", out var t) ? t.GetString() ?? "Window" : "Window";
                var x = root.TryGetProperty("X", out var xx) && xx.ValueKind == JsonValueKind.Number ? xx.GetDouble() : 10;
                var y = root.TryGetProperty("Y", out var yy) && yy.ValueKind == JsonValueKind.Number ? yy.GetDouble() : 10;
                var width = root.TryGetProperty("Width", out var ww) && ww.ValueKind == JsonValueKind.Number ? ww.GetDouble() : 320;
                var height = root.TryGetProperty("Height", out var hh) && hh.ValueKind == JsonValueKind.Number ? hh.GetDouble() : 400;
                if (!string.IsNullOrWhiteSpace(surfaceId))
                {
                    await SendAsync(new OpenWindow(surfaceId, title, x, y, width, height), cancellationToken);
                    return;
                }
            }

            if (type.Contains("CloseWindow", StringComparison.OrdinalIgnoreCase))
            {
                var windowId = root.TryGetProperty("WindowId", out var w) ? w.GetString() : string.Empty;
                if (!string.IsNullOrWhiteSpace(windowId))
                {
                    await SendAsync(new CloseWindow(windowId), cancellationToken);
                    return;
                }
            }

            if (type.Contains("MoveResizeWindow", StringComparison.OrdinalIgnoreCase))
            {
                var windowId = root.TryGetProperty("WindowId", out var w) ? w.GetString() : string.Empty;
                var x = root.TryGetProperty("X", out var xx) && xx.ValueKind == JsonValueKind.Number ? xx.GetDouble() : 0;
                var y = root.TryGetProperty("Y", out var yy) && yy.ValueKind == JsonValueKind.Number ? yy.GetDouble() : 0;
                var width = root.TryGetProperty("Width", out var ww) && ww.ValueKind == JsonValueKind.Number ? ww.GetDouble() : 320;
                var height = root.TryGetProperty("Height", out var hh) && hh.ValueKind == JsonValueKind.Number ? hh.GetDouble() : 400;
                if (!string.IsNullOrWhiteSpace(windowId))
                {
                    await SendAsync(new MoveResizeWindow(windowId, x, y, width, height), cancellationToken);
                    return;
                }
            }

            if (type.Contains("RaiseWindow", StringComparison.OrdinalIgnoreCase))
            {
                var windowId = root.TryGetProperty("WindowId", out var w) ? w.GetString() : string.Empty;
                if (!string.IsNullOrWhiteSpace(windowId))
                {
                    await SendAsync(new RaiseWindow(windowId), cancellationToken);
                    return;
                }
            }

            if (type.Contains("InstallBundle", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Install", StringComparison.OrdinalIgnoreCase))
            {
                var id = root.TryGetProperty("ExperienceId", out var e) ? e.GetString()
                       : root.TryGetProperty("BundleId", out var b) ? b.GetString()
                       : root.TryGetProperty("Id", out var i) ? i.GetString()
                       : string.Empty;

                if (!string.IsNullOrWhiteSpace(id))
                {
                    await InstallBundleAsync(id, cancellationToken);
                    return;
                }
            }

            if (type.Contains("RunExperience", StringComparison.OrdinalIgnoreCase) || type.Contains("Run ", StringComparison.OrdinalIgnoreCase))
            {
                var id = root.TryGetProperty("ExperienceId", out var e) ? e.GetString()
                       : root.TryGetProperty("BundleId", out var b) ? b.GetString()
                       : root.TryGetProperty("Id", out var i) ? i.GetString()
                       : string.Empty;

                if (!string.IsNullOrWhiteSpace(id))
                {
                    await SendAsync(new RunExperience((ExperienceId)id), cancellationToken);
                    return;
                }
            }

            if (type.Contains("DismissAlarm", StringComparison.OrdinalIgnoreCase))
            {
                var id = root.TryGetProperty("AlarmId", out var a) ? a.GetString() : string.Empty;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    await Emit(new DismissAlarm(id));
                    return;
                }
            }

            if (type.Contains("InspectKernelTask", StringComparison.OrdinalIgnoreCase))
            {
                var id = root.TryGetProperty("TaskId", out var t) ? t.GetString() : string.Empty;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    await Emit(new InspectKernelTask(id));
                    return;
                }
            }

            if (type.Contains("AuthorExperiencePrompt", StringComparison.OrdinalIgnoreCase) || type.Contains("author", StringComparison.OrdinalIgnoreCase))
            {
                var prompt = root.TryGetProperty("Prompt", out var p) ? p.GetString()
                           : root.TryGetProperty("prompt", out var p2) ? p2.GetString()
                           : string.Empty;
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    // Drive real LLM (gemma local via registered IChatClient) under the loop: the AgentRequest will cause LlmAgentNeuron
                    // (now in DigitalBrain.Ino per F T2) to use its author_experience + pack_publish tools (inoContent path for pure prompt-authored bundles).
                    // Result: .ino saved, packed to .brain, published to this mp; flutter will see packed-* and marketplace surfaces live via gRPC.
                    await SendAsync(new AgentRequest($"Use the author_experience tool to create a complete .ino experience bundle from this description, then use pack_publish to pack it (with the inoContent) and publish to the marketplace so it is immediately installable by peers or other clusters on LAN: {prompt}"), cancellationToken);
                    await Emit(new NeuronTelemetry(Self, "FlutterCreatorPromptDelivered", new Dictionary<string, string> { ["prompt"] = prompt }));
                    return;
                }
            }

            if (type.Contains("voice", StringComparison.OrdinalIgnoreCase) || type.Contains("Voice", StringComparison.OrdinalIgnoreCase))
            {
                // Voice from flutter recorder (base64 audio in the tap payload) -> decode -> VoiceMessageRecorded synapse.
                // TranscriptionNeuron (activated via launcher) handles STT (whisper.net local) and feeds text to LLM + surfaces.
                var b64 = root.TryGetProperty("audioBase64", out var a) ? a.GetString()
                        : root.TryGetProperty("AudioBase64", out var a2) ? a2.GetString()
                        : string.Empty;
                if (!string.IsNullOrWhiteSpace(b64))
                {
                    var bytes = Convert.FromBase64String(b64);
                    var mime = root.TryGetProperty("mime", out var m) ? m.GetString() ?? "audio/wav" : "audio/wav";
                    await SendAsync(new VoiceMessageRecorded(bytes, mime, null), cancellationToken);
                    await Emit(new NeuronTelemetry(Self, "VoiceMessageReceived", new Dictionary<string, string> { ["bytes"] = bytes.Length.ToString() }));
                    return;
                }
            }

            // Try dynamic deserialization for known synapses (like window frame actions and custom rule events)
            var dict = new Dictionary<string, object>();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals("$type", StringComparison.OrdinalIgnoreCase)) continue;

                if (prop.Value.ValueKind == JsonValueKind.String)
                    dict[prop.Name] = prop.Value.GetString() ?? "";
                else if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    if (prop.Value.TryGetInt32(out var i)) dict[prop.Name] = i;
                    else if (prop.Value.TryGetDouble(out var d)) dict[prop.Name] = d;
                }
                else if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                    dict[prop.Name] = prop.Value.GetBoolean();
                else
                    dict[prop.Name] = prop.Value.ToString();
            }

            var resolvedType = type;
            if (resolvedType.Contains(',')) resolvedType = resolvedType.Split(',')[0].Trim();
            if (resolvedType.Contains('.')) resolvedType = resolvedType.Substring(resolvedType.LastIndexOf('.') + 1);

            var dynamicSynapse = DigitalBrain.InoLang.Domain.Ino.SynapseBinder.TryCreate(resolvedType, dict);
            if (dynamicSynapse != null)
            {
                await Emit(dynamicSynapse);
                return;
            }

            // Fallback: at least make the tap observable on the timeline
            await Emit(new NeuronTelemetry(Self, "ClientTapDelivered", new Dictionary<string, string>
            {
                ["surface"] = tap.SurfaceId,
                ["synapseJson"] = tap.SynapseJson
            }));
        }
        catch (Exception ex)
        {
            await Emit(new NeuronTelemetry(Self, "ClientTapDeliveryFailed", new Dictionary<string, string>
            {
                ["surface"] = tap.SurfaceId,
                ["error"] = ex.Message
            }));
        }
    }

    public async Task PublishBundleAsync(string bundleId, string? description = null, CancellationToken cancellationToken = default)
    {
        var s = _profile.State;
        var bid = (BundleId)bundleId;
        if (!s.PublishedBundles.Contains(bid))
        {
            s.PublishedBundles.Add(bid);
            if (!string.IsNullOrWhiteSpace(description))
                s.CustomState[$"bundle:{bundleId}"] = description;
        }
        await _profile.WriteStateAsync(cancellationToken);

        var pub = new BundlePublished(bid, description);
        await Emit(pub);
    }

    public Task<IReadOnlyList<string>> ListPublishedBundlesAsync(CancellationToken cancellationToken = default)
    {
        var fromState = _profile.State.PublishedBundles.Select(b => (string)b).ToList();
        return Task.FromResult<IReadOnlyList<string>>(fromState);
    }

    public Task<IReadOnlyList<string>> ListInstalledBundlesAsync(CancellationToken cancellationToken = default)
    {
        var fromState = _profile.State.InstalledBundles.Select(b => (string)b).ToList();
        return Task.FromResult<IReadOnlyList<string>>(fromState);
    }

    public async Task UninstallBundleAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        var s = _profile.State;
        var toUn = (BundleId)bundleId;

        // system: true from the bundle's .ino header (IsSystem in InoExperience at install time). Data driven, not hardcoded list.
        // Refuse with surface; journal untouched; no N-1 shrink for system substrate.
        bool isSystem = s.CustomState.TryGetValue($"system:{bundleId}", out var sysFlag) && string.Equals(sysFlag, "true", StringComparison.OrdinalIgnoreCase);
        if (isSystem)
        {
            // Direct UiSurface for system uninstall-refused removed (single source); emit telemetry watched by shell.ino rule for surface.
            await Emit(new NeuronTelemetry(Self, "UninstallRefused", new Dictionary<string, string> { ["bundleId"] = bundleId }));
            return;
        }

        s.InstalledBundles.Remove(toUn);
        if (s.ContractBundles != null)
            s.ContractBundles.Remove(toUn);
        s.CustomState.Remove($"system:{bundleId}");

        var ruleHost = GrainFactory.GetGrain<IRuleHostNeuron>(this.GetPrimaryKeyString());
        await ruleHost.RemoveRuleSetAsync(bundleId, cancellationToken);

        await _profile.WriteStateAsync(cancellationToken);

        // Single emit so Shell (and others) clean placements exactly once; N-1 already done via Contract/Installed removal.
        await Emit(new BundleUninstalled(bundleId));
    }

    public async Task InstallBundleAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        var installed = await InstallBundleAsync(new InstallBundle((BundleId)bundleId), cancellationToken);
        var s = _profile.State;
        s.CustomState[$"installed-via-bundle:{bundleId}"] = DateTimeOffset.UtcNow.ToString("o");
        await _profile.WriteStateAsync(cancellationToken);
    }

    public async Task RunExperienceAsync(string experienceId, CancellationToken cancellationToken = default)
    {
        await SendAsync(new RunExperience((ExperienceId)experienceId), cancellationToken);
    }

    public async Task HandleAsync(RunExperience synapse, CancellationToken cancellationToken)
    {
        var id = synapse.Id.Value;
        var s = _profile.State;
        var contentKey = $"bundle-content:{id}";
        if (!s.CustomState.TryGetValue(contentKey, out var contentText) || string.IsNullOrWhiteSpace(contentText))
        {
            await Emit(new NeuronTelemetry(Self, "RunExperienceMissingContent", new Dictionary<string, string> { ["id"] = id }));
            return;
        }

        DigitalBrain.InoLang.Domain.Ino.InoExperience? exp = null;
        var isYaml = contentText.Contains("schemaVersion: \"os-on-yaml/", StringComparison.OrdinalIgnoreCase);
        try
        {
            exp = isYaml
                ? DigitalBrain.InoLang.Domain.Yaml.YamlParser.Parse(contentText)
                : DigitalBrain.InoLang.Domain.Ino.InoParser.Parse(contentText);
        }
        catch { }

        if (exp == null)
        {
            await Emit(new NeuronTelemetry(Self, "RunExperienceParseFailed", new Dictionary<string, string> { ["id"] = id, ["yaml"] = isYaml.ToString() }));
            return;
        }

        // ensure rules registered for this downloaded declarative (runtime re-parse path, additive to install-time)
        if (exp.Rules is { Length: > 0 })
        {
            var ruleHost = GrainFactory.GetGrain<IRuleHostNeuron>(this.GetPrimaryKeyString());
            var rs = new DigitalBrain.InoLang.Domain.Ino.RuleSet(exp.Rules, exp.Emits ?? Array.Empty<string>());
            await ruleHost.InstallRulesAsync(id, rs, cancellationToken);
        }

        // primary entry inferred from parsed (prefer first rule that shows a card for immediate UI surface; else first On)
        var showRule = exp.Rules?.FirstOrDefault(r => r.Do != null && r.Do.OfType<DigitalBrain.InoLang.Domain.Ino.ShowCardRuleStatement>().Any());
        var entryOn = showRule?.On ?? exp.Rules?.FirstOrDefault()?.On ?? "Launch";

        // emit starter so RuleHost timeline wildcard executes matching rule -> RuleMatched + show card UiSurface (the declarative definition at runtime)
        // concrete for standup-reminder yaml (its show is on SetAlarm); binder for others. Starter stamped for lineage; journals capture; grants/replay/N+1 unaffected.
        Synapse? starter = null;
        if (string.Equals(entryOn, "SetAlarm", StringComparison.OrdinalIgnoreCase))
        {
            starter = new SetAlarm(0, "standup");
        }
        else
        {
            starter = DigitalBrain.InoLang.Domain.Ino.SynapseBinder.TryCreate(entryOn, new Dictionary<string, object>());
        }

        if (starter != null)
        {
            await Emit(starter.Stamp(Self, synapse));
        }

        // For the rich behavioral gmail example (expressive .ino + chart viz), launching the experience
        // immediately drives the data path so the widget "pops" with the visualization + auth button.
        // User can work the flow entirely through the neurons/synapses declared in the .ino (or yaml).
        if (id == "gmail-senders-chart")
        {
            await Emit(new GmailSenderCountsRequest());
            // Emit direct BarChart surface so the live UI (Flutter widget tree) contains the chart visualization
            // when the experience is launched via Run / ino.cs REPL. This makes the expected UI (bars for top senders)
            // verifiable via dart mcp get_widget_tree.
            var demoBars = new[]
            {
                new Bar("newsletter@company.com", 47, "#00E5D1"),
                new Bar("boss@work.com", 31, "#9E00FF"),
                new Bar("team@project.org", 28, "#00E5D1"),
                new Bar("alerts@service.io", 19, "#9E00FF"),
                new Bar("friend@gmail.com", 14, "#00E5D1"),
            };
            await Emit(new UiSurface("gmail-senders-chart", Self, new Card("Top Senders by Volume", new BarChart("Emails Received", demoBars))));
            // Make Run (ino.cs auto + marketplace) produce draggable floating via existing OpenWindow + Shell window state + ui-windows surface.
            // Content pulled from the recent UiSurface by SurfaceId in ShellNeuron EmitCurrentWorkspace (WindowFrame.Content from _recentSurfaces).
            await Emit(new OpenWindow("gmail-senders-chart", "📧 Mail", 80, 80, 540, 380));
        }

        if (id == "telegram-bot")
        {
            // Minimal TG stub surface for RunExperience testability (per plan). Hyperlink opens the flutter viewer
            // in tg mode (reuses existing draggable Mail floating + IAsync streaming already wired).
            // Real neuron (TelegramConnectorNeuron) will later do Bot API calls + Tg* synapses.
            var tgLink = "http://localhost:8080/flutter?tg=1&exp=gmail-senders-chart&mode=floating";
            var tgContent = new Column(new UiWidget[]
            {
                new Text("Telegram Bot (demo) — grant + vault for real token."),
                new Text("Opens current client (Mail floatings + progressive stream) as TG WebApp."),
                new Hyperlink("Open Mail as TG WebApp (floating)", tgLink)
            });
            await Emit(new UiSurface("telegram-bot", Self, new Card("TG Bot Ready", tgContent)));
        }

        await Emit(new NeuronTelemetry(Self, "ExperienceRuntimeLaunched", new Dictionary<string, string>
        {
            ["id"] = id,
            ["entry"] = entryOn,
            ["parser"] = isYaml ? "yaml" : "ino",
            ["source"] = "bundle-content",
            ["hasRules"] = (exp.Rules?.Length > 0).ToString()
        }));
    }

    public Task<IReadOnlyList<Synapse>> GetRecentHistoryAsync(int max = 10, CancellationToken cancellationToken = default)
    {
        return GetJournalHistoryAsync(max, cancellationToken);
    }

    public new Task<IReadOnlyList<Synapse>> GetFullJournalAsync(CancellationToken cancellationToken = default)
    {
        return base.GetFullJournalAsync(cancellationToken);
    }

    // Stage 1: back the custom per-grain IDurableList journals with IPersistentState snapshots (using the "Default" storage,
    // which is Redis for the root kernel under Aspire). This gives real durability for causal history/replay across
    // re-activation and kernel restarts without changing the custom journaling path the user requested.
    // The NeuronState already declared Incoming/Outgoing list fields for this purpose.
    protected override async Task RestoreJournalsFromSnapshotAsync()
    {
        var s = _profile.State;
        if (s.Incoming.Count > 0 && Incoming.Count == 0)
        {
            foreach (var item in s.Incoming) Incoming.Add(item);
        }
        if (s.Outgoing.Count > 0 && Outgoing.Count == 0)
        {
            foreach (var item in s.Outgoing) Outgoing.Add(item);
        }
    }

    protected override async Task SnapshotJournalsToStateAsync()
    {
        var s = _profile.State;
        s.Incoming.Clear();
        s.Incoming.AddRange(Incoming);
        s.Outgoing.Clear();
        s.Outgoing.AddRange(Outgoing);
        await _profile.WriteStateAsync();
    }

    public async Task<WorldConnectionInfo> StartWorldAsync(string worldId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worldId)) worldId = "primary";
        var opts = new DigitalBrainStartOptions { Mode = DigitalBrainLaunchMode.AspireHosted, WorldId = worldId };
        var launchResult = await DigitalBrainLauncher.LaunchAsync(opts, cancellationToken); // awaited (P0#3) so brain neuron sees durable orchestration outcome instead of discarding
        var cluster = $"digitalbrain-{SanitizeForWorld(worldId)}";
        WorldConnectionInfo info;
        string? capturedDash = null;
        if (launchResult is RealDigitalBrainClient real && real.CurrentWorld is { } cw)
        {
            capturedDash = real.DashboardUrl;
            // use the actual allocated gateway + dashboard from this launch (no more placeholder)
            info = new WorldConnectionInfo(worldId, cw.ClusterId, cw.ServiceId, cw.GatewayAddress, DashboardUrl: capturedDash);
        }
        else
        {
            info = new WorldConnectionInfo(worldId, cluster, "digitalbrain", "127.0.0.1:30200");
        }
        var s = _profile.State;
        var launchStatus = launchResult is RealDigitalBrainClient ? "success" : "failed-marker";
        var gwForStore = info.GatewayAddress;
        s.CustomState[$"world:{worldId}"] = $"{info.ClusterId}|{info.ServiceId}|{gwForStore}|{capturedDash ?? ""}|status:{launchStatus}";
        s.CustomState[$"last-world-launch:{worldId}"] = $"{DateTimeOffset.UtcNow:o}|{launchStatus}";
        await _profile.WriteStateAsync(cancellationToken);
        await Emit(info);

        var aspire = GrainFactory.GetGrain<IAspire>(Brain.WellKnownKey);
        try
        {
            await DigitalBrainLauncher.EnsureForDomainAsync(GrainFactory, worldId, cancellationToken);
            await aspire.RestartDomainKernelAsync(worldId, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"StartWorldAsync IAspire restart for {worldId} logged: {ex.Message}");
        }

        return info;
    }

    public Task<WorldConnectionInfo?> GetWorldConnectionAsync(string worldId, CancellationToken cancellationToken = default)
    {
        var s = _profile.State;
        if (s.CustomState.TryGetValue($"world:{worldId}", out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split('|');
            if (parts.Length >= 3)
            {
                string? dash = null;
                if (parts.Length > 3)
                {
                    var p3 = parts[3];
                    if (!string.IsNullOrWhiteSpace(p3) && !p3.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                        dash = p3;
                }
                return Task.FromResult<WorldConnectionInfo?>(new WorldConnectionInfo(worldId, parts[0], parts[1], parts[2], dash));
            }
        }
        return Task.FromResult<WorldConnectionInfo?>(null);
    }

    public Task<WorldConnectionInfo?> GetCurrentWorldAsync(CancellationToken cancellationToken = default)
    {
        var worldFromEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_WORLD_ID");
        if (!string.IsNullOrWhiteSpace(worldFromEnv))
        {
            var cluster = Environment.GetEnvironmentVariable("DIGITALBRAIN_CLUSTER_ID") ?? $"digitalbrain-{SanitizeForWorld(worldFromEnv)}";
            var svc = Environment.GetEnvironmentVariable("DIGITALBRAIN_SERVICE_ID") ?? "digitalbrain";
            var gwPort = Environment.GetEnvironmentVariable("DIGITALBRAIN_GATEWAY_PORT") ?? "30000";
            var dash = Environment.GetEnvironmentVariable("DIGITALBRAIN_DASHBOARD_URL");
            return Task.FromResult<WorldConnectionInfo?>(new WorldConnectionInfo(worldFromEnv, cluster, svc, $"127.0.0.1:{gwPort}", dash));
        }
        return Task.FromResult<WorldConnectionInfo?>(null);
    }

    public async Task<WorldConfig?> GetWorldConfigAsync(string? worldId = null, CancellationToken cancellationToken = default)
    {
        var w = worldId ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_WORLD_ID");
        if (string.IsNullOrWhiteSpace(w)) return null;
        var conn = await GetWorldConnectionAsync(w, cancellationToken);
        if (conn is null) return null;
        var gemma = Environment.GetEnvironmentVariable("GEMMA_MODEL");
        var nemo = Environment.GetEnvironmentVariable("NEMOTRON_MODEL");
        return new WorldConfig(conn.WorldId, conn.ClusterId, conn.ServiceId, conn.GatewayAddress, gemma, nemo);
    }

    static string SanitizeForWorld(string s) => new string(s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());

    public async Task<WorldConnectionInfo> ForkBrainAsync(string parentBrainKey, string newBrainName, DateTimeOffset? upTo = null, CancellationToken cancellationToken = default)
    {
        // S06 dedup decision: dedup on SynapseId during replay to avoid duplicate events in the child timeline (fork rides the quarantine/key-isolation machinery for risky experiments).
        var parent = GrainFactory.GetGrain<IDigitalBrain>(parentBrainKey);
        var journal = await parent.GetFullJournalAsync(cancellationToken);
        var filtered = journal.Where(s => upTo == null || s.Metadata.Timestamp <= upTo.Value).ToList();
        var seen = new HashSet<SynapseId>();
        var deduped = new List<Synapse>();
        foreach (var syn in filtered)
        {
            if (seen.Add(syn.Metadata.SynapseId))
                deduped.Add(syn);
        }
        var forkKey = newBrainName;
        var forkBrain = GrainFactory.GetGrain<IDigitalBrain>(forkKey);
        foreach (var syn in deduped)
        {
            await forkBrain.SendAsync(syn, cancellationToken);
        }
        // Ride quarantine machinery: start an isolated world for the fork (similar to StartQuarantineWorld).
        var info = await StartWorldAsync("fork-" + newBrainName, cancellationToken);
        var state = _profile.State;
        state.CustomState[$"fork:{newBrainName}"] = $"{parentBrainKey}|{upTo?.ToString() ?? "full"}|deduped:{seen.Count}";
        await _profile.WriteStateAsync(cancellationToken);
        await Emit(new NeuronTelemetry(Self, "BrainForked", new Dictionary<string, string> { ["parent"] = parentBrainKey, ["fork"] = newBrainName, ["deduped"] = seen.Count.ToString() }));
        return info;
    }

    public async Task HandleAsync(DigitalBrain.Os.Domain.Events.ForkBrain synapse, CancellationToken cancellationToken)
    {
        await ForkBrainAsync(synapse.ParentBrainKey, synapse.NewBrainName, synapse.UpTo, cancellationToken);
    }

    private async Task ResolveBundleSourceContentAsync(InstallBundle command, BundleId toInstallId, string id, string? targetDomain, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.SourcePathOrUri))
        {
            await Emit(new NeuronTelemetry(Self, "BundleSourceResolution", new Dictionary<string, string> { ["source"] = command.SourcePathOrUri, ["id"] = id, ["targetDomain"] = targetDomain ?? "global" }));
            try
            {
                string? content = null;
                if (command.SourcePathOrUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    content = await http.GetStringAsync(command.SourcePathOrUri, cancellationToken);
                }
                else if (File.Exists(command.SourcePathOrUri))
                {
                    content = await File.ReadAllTextAsync(command.SourcePathOrUri, cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var s = _profile.State;
                    s.CustomState[$"bundle-content:{toInstallId}"] = content;
                    await Emit(new NeuronTelemetry(Self, "BundleContentResolved", new Dictionary<string, string>
                    {
                        ["id"] = toInstallId.Value,
                        ["length"] = content.Length.ToString(),
                        ["source"] = command.SourcePathOrUri
                    }));
                }
            }
            catch (Exception ex)
            {
                await Emit(new NeuronTelemetry(Self, "BundleResolutionFailed", new Dictionary<string, string> { ["source"] = command.SourcePathOrUri, ["error"] = ex.Message }));
            }
        }
    }

    private async Task<BrainIdentity> EnsureBrainIdentityAsync(CancellationToken cancellationToken)
    {
        var s = _profile.State;
        if (string.IsNullOrWhiteSpace(s.BrainPublicKeyBase64) || string.IsNullOrWhiteSpace(s.BrainPrivateKeyBase64))
        {
            var gen = new Ed25519KeyPairGenerator();
            gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            var priv = (Ed25519PrivateKeyParameters)pair.Private;
            var pub = (Ed25519PublicKeyParameters)pair.Public;
            s.BrainPrivateKeyBase64 = Convert.ToBase64String(priv.GetEncoded());
            s.BrainPublicKeyBase64 = Convert.ToBase64String(pub.GetEncoded());
            await _profile.WriteStateAsync(cancellationToken);
        }
        var pubB = Convert.FromBase64String(s.BrainPublicKeyBase64!);
        var fp = Convert.ToHexStringLower(SHA256.HashData(pubB))[..16];
        return new BrainIdentity(s.BrainPublicKeyBase64!, fp, DateTimeOffset.UtcNow);
    }

    public async Task<BrainIdentity> GetIdentityAsync(CancellationToken cancellationToken = default)
    {
        return await EnsureBrainIdentityAsync(cancellationToken);
    }

    public async Task<string> SignAsync(string data, CancellationToken cancellationToken = default)
    {
        var s = _profile.State;
        if (string.IsNullOrWhiteSpace(s.BrainPrivateKeyBase64)) await EnsureBrainIdentityAsync(cancellationToken);
        var priv = new Ed25519PrivateKeyParameters(Convert.FromBase64String(s.BrainPrivateKeyBase64!));
        var signer = new Ed25519Signer();
        signer.Init(true, priv);
        var bytes = Encoding.UTF8.GetBytes(data);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        var sig = signer.GenerateSignature();
        return Convert.ToBase64String(sig);
    }

    public async Task HandleAsync(StartQuarantineWorld synapse, CancellationToken cancellationToken)
    {
        var qWorld = "quarantine-" + synapse.ExperienceId.Value.Replace("@", "-");
        await Emit(new NeuronTelemetry(Self, "QuarantineStarted", new Dictionary<string, string> { ["q"] = qWorld, ["id"] = synapse.ExperienceId.Value }));
        var qBrain = GrainFactory.GetGrain<IDigitalBrain>(qWorld);
        var installCmd = new InstallBundle((BundleId)synapse.ExperienceId.Value, synapse.PeerAddress, TargetDomainId: qWorld);
        await qBrain.InstallBundleAsync(installCmd, cancellationToken);
        await Emit(new NeuronTelemetry(Self, "QuarantineInstall", new Dictionary<string, string> { ["q"] = qWorld, ["id"] = synapse.ExperienceId.Value }));
        var promoted = new QuarantinePromoted(synapse.ExperienceId, qWorld, Green: true);
        await Emit(promoted);
        // Quarantine green surface removed (direcancellationToken); telemetry "QuarantineGreen" + shell.ino NeuronTelemetry rule produces card.
        await Emit(new NeuronTelemetry(Self, "QuarantineGreen", new Dictionary<string, string> { ["id"] = synapse.ExperienceId.Value }));
    }

    public async Task HandleAsync(UpdateBundle synapse, CancellationToken cancellationToken)
    {
        var mkt = GrainFactory.GetGrain<IMarketplace>(this.GetPrimaryKeyString());
        var listed = await mkt.ListAsync(cancellationToken);
        var target = listed.FirstOrDefault(l => l.Manifest.Id == (string)synapse.ExperienceId && (synapse.TargetVersion == null || l.Manifest.Version == synapse.TargetVersion));
        if (target is null) return;
        var dl = synapse.PeerAddress is null ? await mkt.InstallListedAsync(target.Manifest.Id, cancellationToken) : await mkt.InstallFromPeerAsync(synapse.PeerAddress, target.Manifest.Id, cancellationToken);
        await Emit(new NeuronTelemetry(Self, "BundleUpdated", new Dictionary<string, string> { ["id"] = target.Manifest.Id, ["ver"] = target.Manifest.Version }));
        // Update result surface removed; use telemetry "BundleUpdateResult" observed by shell rule for system notification card.
        await Emit(new NeuronTelemetry(Self, "BundleUpdateResult", new Dictionary<string, string> { ["id"] = target.Manifest.Id, ["ver"] = target.Manifest.Version, ["hash"] = target.Manifest.ContentHash[..8] }));
    }

    public Task HandleAsync(UninstallBundle synapse, CancellationToken cancellationToken) => UninstallBundleAsync(synapse.BundleId, cancellationToken);
}
