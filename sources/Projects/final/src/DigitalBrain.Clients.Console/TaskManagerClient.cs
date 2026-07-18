using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using DigitalBrain.Hosting.DigitalBrain;
using Hex1b;
using Hex1b.Widgets;
using Orleans;
using Orleans.Streams;
using System.Diagnostics;

namespace DigitalBrain.Clients.ConsoleClient;

public static class TaskManagerClient
{
    public static async Task RunAsync(IClusterClient? clusterClient, IDigitalBrain? brain, bool connected, string? aspireDashboardUrl = null, string? localPeerAddress = null, string? initialBrainKey = null, CancellationToken cancellationToken = default)
    {
        var state = new ClientState { Connected = connected, AspireDashboardUrl = aspireDashboardUrl };
        if (!string.IsNullOrWhiteSpace(initialBrainKey))
        {
            var p = initialBrainKey.Split('/', 2);
            state.Username = p[0];
            state.CurrentBrain = p.Length > 1 ? p[1] : "main";
        }
        if (!string.IsNullOrWhiteSpace(localPeerAddress))
        {
            state.LastPeer = localPeerAddress;
            state.MarketPeerInput = localPeerAddress;
        }
        state.SetClusterClient(clusterClient);

        // Best-effort real active Aspire dashboard for the sim/fast path (dotnet run start.cs has no AppHost).
        // Launches standalone `aspire dashboard run` (detached), captures the printed login URL (like launcher does for worlds),
        // sets it so Settings/header show a valid clickable link for "current cluster". Falls back to sim note if no aspire CLI.
        // For real aspire-hosted clusters (via launcher or AppHost), the real per-cluster URL comes from WorldConnectionInfo.DashboardUrl.
        if (string.IsNullOrWhiteSpace(state.AspireDashboardUrl) && connected)
        {
            _ = state.EnsureActiveAspireDashboardForCurrentClusterAsync();
        }

        StreamSubscriptionHandle<Synapse>? subscription = null;
        if (clusterClient is not null)
        {
            subscription = await clusterClient
                .GetStreamProvider(SynapseStream.ProviderName)
                .Timeline()
                .SubscribeAsync((synapse, _) =>
                {
                    switch (synapse)
                    {
                        case UiSurface surface:
                            state.ApplySurface(surface);
                            break;
                        case WorkspaceChanged w:
                            state.ApplyWorkspace(w);
                            break;
                        case AgentRequest req:
                            state.AddChat(true, req.Prompt);
                            break;
                        case AgentResponse resp:
                            state.AddChat(false, resp.Content);
                            break;
                        case WorldConnectionInfo w:
                            state.ApplyWorld(w);
                            break;
                        default:
                            break;
                    }
                    return Task.CompletedTask;
                });
        }

        if (brain is not null)
        {
            try
            {
                var b = state.GetCurrentBrain() ?? brain;
                var history = await b.GetFullJournalAsync(cancellationToken);
                state.SeedFromHistory(history);
            }
            catch (Exception ex) { state.AddNotification("history seed failed: " + ex.Message); }
        }

        // Real-app theming per hex1b.dev/guide/theming + web examples: Hex1bAppOptions (Theme for colors/borders/widget styling on Tab/Button/Markdown/InfoBar).
        // Polished dark "real app" (accents for dashboard links + start buttons). Use new options (exact Hex1bThemes.Sunset may vary by pkg version 0.164+; defaults + future set still give themed real-app look).
        var appOptions = new Hex1bAppOptions(); // Theme = Hex1bThemes.Sunset when API resolves in the referenced Hex1b; see plan + web for customization.

        using var app = new Hex1bApp(ctx => ctx.VStack(v => new Hex1bWidget[]
        {
            // Header bar (real app chrome, per plan + hex1b theming): current cluster label + dashboard for current cluster (real active or launch action for start.cs sim).
            // Uses only proven widgets (Text/Rescue Markdown for links, Button).
            ctx.HStack(h => new Hex1bWidget[]
            {
                h.Text("DigitalBrain"),
                h.Text(" | "),
                h.Text(state.GetCurrentClusterLabel(localPeerAddress)),
                h.Text(" | "),
                BuildDashboardHeader(h, state),
                h.Text(" | "),
                h.Text(localPeerAddress ?? "local"),
                h.Text(" | q quit")
            }),
            ctx.TabPanel(tp => new[]
            {
                tp.Tab("Home", t => BuildHomeTab(t, state, clusterClient, brain)), // OS2 probe: inside existing chrome; widgets column of titled Borders (via unchanged SurfaceRenderer on pinned from Workspace), main area, dock List
                tp.Tab("Cluster", t => BuildClusterTab(t, state, clusterClient, brain, localPeerAddress)), // renamed/enhanced Settings: dashboard + worlds + working start buttons
                tp.Tab("Ino", t => BuildInoTab(t, state, clusterClient, brain)),
                tp.Tab("Creator", t => BuildCreatorTab(t, state, clusterClient, brain)),
                tp.Tab("Marketplace", t => BuildMarketplaceTab(t, state, clusterClient, brain))
            }).Fill(),
            ctx.InfoBar(ib =>
            {
                var children = new List<IInfoBarChild>
                {
                    ib.Section(state.Connected ? "connected" : "sim")
                };
                if (!string.IsNullOrWhiteSpace(state.AspireDashboardUrl))
                {
                    children.Add(ib.Section($"| dashboard: {ShortenUrl(state.AspireDashboardUrl)}"));
                }
                else
                {
                    children.Add(ib.Section("| local sim cluster (no dashboard)"));
                }
                children.Add(ib.Spacer());
                children.Add(ib.Section("Tab/Enter • buttons + live timeline"));
                return children.ToArray();
            })
        }), appOptions);

        try { await app.RunAsync(cancellationToken); }
        finally
        {
            if (subscription is not null)
            {
                try { await subscription.UnsubscribeAsync(); }
                catch (Exception ex) { Console.Error.WriteLine("unsub failed: " + ex.Message); }
            }
        }
    }

    // Small self-explanatory header for real-app dashboard presence (current cluster). Accepts the h ctx from HStack for compatibility.
    static Hex1bWidget BuildDashboardHeader(WidgetContext<HStackWidget> h, ClientState state)
    {
        if (!string.IsNullOrWhiteSpace(state.AspireDashboardUrl))
        {
            return h.Rescue(h.Markdown($"[aspire dashboard]({state.AspireDashboardUrl})"));
        }
        return h.Button("launch dashboard").OnClick(async _ =>
        {
            var url = await state.EnsureActiveAspireDashboardForCurrentClusterAsync();
            if (!string.IsNullOrWhiteSpace(url))
            {
                state.AddNotification($"dashboard active: {url}");
            }
            else
            {
                state.AddNotification("no aspire cli or launch failed - run `aspire dashboard run` manually");
            }
        });
    }

    static string ShortenUrl(string url) => url.Length > 40 ? url[..37] + "..." : url;

    static Hex1bWidget[] BuildInoTab(WidgetContext<VStackWidget> t, ClientState state, IClusterClient? cc, IDigitalBrain? brain)
    {
        var widgets = new List<Hex1bWidget>();

        if (!string.IsNullOrWhiteSpace(state.AspireDashboardUrl))
        {
            widgets.Add(t.Rescue(t.Markdown($"[aspire dashboard for cluster]({state.AspireDashboardUrl})")));
        }

        widgets.Add(t.Text("Ino chat (journals + live surfaces) — Ino knows DigitalBrain final/ (canonical) + lineage (ino/ E2E, IAW/ hosting) + current UI (Settings cluster switch, peer install rows from query, marketplace surface buttons)"));

        // chat history (seed + live)
        foreach (var line in state.ChatLines.TakeLast(8))
            widgets.Add(t.Text(line));

        // routed surfaces for ino (review: and unknown), Rescue wrapped, renderer for Markdown support
        foreach (var surf in state.AskSurfaces.TakeLast(3))
        {
            widgets.Add(t.Rescue(SurfaceRenderer.Render(t, surf.Root, action => state.Fire(action, brain))));
        }

        widgets.Add(t.HStack(h => new Hex1bWidget[]
        {
            h.Text("ino> "),
            h.TextBox(state.ChatInput).OnTextChanged(e => state.ChatInput = e.NewText),
            h.Button("Send").OnClick(_ => state.SendChat(cc, brain))
        }));

        // notifications (replaces LastMsg)
        foreach (var n in state.Notifications.TakeLast(3))
            widgets.Add(t.Text(n));

        return widgets.ToArray();
    }

    // Polished "real app" Cluster tab (from-scratch redesign per plan + hex1b theming).
    // - Current cluster dashboard card (real active url or launch action for start.cs sim path to make url active/valid).
    // - Worlds as clean rows with per-cluster DashboardUrl (from WorldConnectionInfo synapse when the cluster was aspire-launched).
    // - Prominent primary buttons for start worlds (progress/feedback via notifs, live list update via timeline + ApplyWorld).
    // - No trash: purposeful, short, themed (Sunset accents for links/buttons), cards/sections. "Current cluster" uses entry or world dash.
    static Hex1bWidget[] BuildClusterTab(WidgetContext<VStackWidget> t, ClientState state, IClusterClient? cc, IDigitalBrain? brain, string? localPeerAddress)
    {
        var widgets = new List<Hex1bWidget>();

        // Current cluster header card (real app feel).
        widgets.Add(t.Text("Current Cluster"));
        var clusterLabel = state.GetCurrentClusterLabel(localPeerAddress);
        widgets.Add(t.Rescue(t.Markdown($"**{clusterLabel}**")));

        // Aspire dashboard for current cluster (the key fix + real url only; launch makes it active even from plain start.cs).
        if (!string.IsNullOrWhiteSpace(state.AspireDashboardUrl))
        {
            widgets.Add(t.Rescue(t.Markdown($"Aspire dashboard (current cluster): [{state.AspireDashboardUrl}]({state.AspireDashboardUrl})")));
            widgets.Add(t.Button("Open in browser (copy url)").OnClick(_ => state.AddNotification($"copy/open: {state.AspireDashboardUrl}")));
        }
        else
        {
            widgets.Add(t.Text("No active Aspire dashboard for this cluster (sim path)."));
            widgets.Add(t.Button("Launch Aspire Dashboard (standalone, active url)").OnClick(async _ =>
            {
                var u = await state.EnsureActiveAspireDashboardForCurrentClusterAsync();
                if (!string.IsNullOrWhiteSpace(u)) state.AddNotification($"dashboard now active: {u}");
            }));
        }

        widgets.Add(t.Text(" ")); // spacer

        // Worlds / clusters management (per-cluster dashboard from WorldConnectionInfo).
        widgets.Add(t.Text("Worlds / Clusters (live from timeline + StartWorld)"));
        var worlds = state.KnownWorlds;
        if (worlds.Count == 0)
        {
            widgets.Add(t.Text("(no worlds yet — use buttons below; real aspire runs populate with dashboard + gw)"));
        }
        else
        {
            foreach (var wld in worlds.TakeLast(8))
            {
                // Card-like row (themed borders via surrounding + accent for dash).
                widgets.Add(t.HStack(hh => new Hex1bWidget[]
                {
                    hh.Text($"{wld.WorldId} | {wld.GatewayAddress}"),
                    hh.Button("target peer").OnClick(_ => state.SetTargetPeerFromWorld(wld))
                }));
                if (!string.IsNullOrWhiteSpace(wld.DashboardUrl))
                {
                    widgets.Add(t.Rescue(t.Markdown($"  dashboard: [{wld.DashboardUrl}]({wld.DashboardUrl})")));
                }
            }
        }

        widgets.Add(t.Text(" "));

        // Prominent action buttons (real app primary actions; fixed to actually work + give feedback).
        widgets.Add(t.HStack(h => new Hex1bWidget[]
        {
            h.Button("Refresh current").OnClick(_ => state.DoRefreshCurrentWorld(brain)),
            h.Button("Start personal (root)").OnClick(_ => state.DoStartWorld(brain, "root")),
            h.Button("Start work (example-world)").OnClick(_ => state.DoStartWorld(brain, "example-world"))
        }));

        // Minimal lineage (moved out of main view; ask Ino for full).
        widgets.Add(t.Text("Lineage: final/ (canonical) • ino/ (E2E ref) • IAW/ (hosting ref) — ask in Ino tab"));

        foreach (var n in state.Notifications.TakeLast(3))
            widgets.Add(t.Text(n));

        return widgets.ToArray();
    }

    static Hex1bWidget[] BuildCreatorTab(WidgetContext<VStackWidget> t, ClientState state, IClusterClient? cc, IDigitalBrain? brain)
    {
        var editor = t.TextBox(state.CreatorInoContent)
            .OnTextChanged(e => state.CreatorInoContent = e.NewText);

        var row = t.HStack(h => new[]
        {
            h.Button("Pack").OnClick(_ => state.DoPack(cc)),
            h.Button("Publish").OnClick(_ => state.DoPublish(brain))
        });

        var notifs = state.Notifications.TakeLast(2).Select(n => (Hex1bWidget)t.Text(n)).ToArray();

        return new Hex1bWidget[] { editor, row }.Concat(notifs).ToArray();
    }

    static Hex1bWidget[] BuildMarketplaceTab(WidgetContext<VStackWidget> t, ClientState state, IClusterClient? cc, IDigitalBrain? brain)
    {
        var w = new List<Hex1bWidget> { t.Text("Marketplace surface (routed via InstallFromMarketplace synapse) + peer query") };

        // the "marketplace" surface rendered generically (Rescue + renderer with Markdown support)
        if (state.MarketplaceSurface is { } mkt)
            w.Add(t.Rescue(SurfaceRenderer.Render(t, mkt.Root, action => state.Fire(action, brain))));
        else
            w.Add(t.Text("(no marketplace surface yet — seed runs on start; query peer or chat to trigger)"));

        // peer query -> ClientActions, results in tab-local peer list labeled as such
        w.Add(t.HStack(h => new Hex1bWidget[]
        {
            h.Text("peer> "),
            h.TextBox(state.MarketPeerInput).OnTextChanged(e => state.MarketPeerInput = e.NewText),
            h.Button("Query peer").OnClick(_ => state.DoMarketPeer(cc))
        }));

        // Peer results as inbox-style actionable rows (aligns with hex1b widgets-nodes + previous ino timeline/inbox rows: immutable row widgets declare content+action, nodes reconcile state like focus).
        // Each qualifying listing row = HStack(Text(desc), Button("Install from peer")) — uses LastPeer for InstallFromPeerAsync.
        // Local installs use the surface Install buttons above (InstallFromMarketplace synapse -> InstallListed).
        foreach (var line in state.PeerListings.TakeLast(8))
        {
            var id = TryParseListingId(line);
            if (id is not null && !string.IsNullOrWhiteSpace(state.LastPeer))
            {
                w.Add(t.HStack(hh => new Hex1bWidget[]
                {
                    hh.Text(line),
                    hh.Button("Install from peer").OnClick(_ => state.DoInstallFromPeerId(cc, id))
                }));
            }
            else
            {
                w.Add(t.Text(line));
            }
        }

        // Explicit "any id from peer/local" form — completes the peer/local install story without magic ids.
        // Uses same row pattern for consistency (inbox-like: label + input + actions).
        w.Add(t.HStack(h => new Hex1bWidget[]
        {
            h.Text("install id> "),
            h.TextBox(state.MarketInstallId).OnTextChanged(e => state.MarketInstallId = e.NewText),
            h.Button("from peer").OnClick(_ => state.DoInstallIdFromPeer(cc)),
            h.Button("local listed").OnClick(_ => state.DoInstallIdLocal(cc))
        }));

        foreach (var n in state.Notifications.TakeLast(2))
            w.Add(t.Text(n));

        return w.ToArray();
    }

    private static string? TryParseListingId(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var trimmed = line.Trim();
        if (trimmed.StartsWith('(') || trimmed.StartsWith("peer ", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("no listings", StringComparison.OrdinalIgnoreCase))
            return null;
        var first = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first) || first.Contains(':') || first.Contains('@')) return null;
        // Typical: "my-exp v0.1.0 • ..." or "id vX ..."
        return first;
    }

    // OS5 pure renderer (regions as chrome: widgets column titled via SurfaceRenderer on pinned from ws, main, dock; D5 prefix demoted, null->main. Legacy tabs optional; flows re-verified through workspace. SurfaceRenderer unchanged. UI content for OS experiences now defined via rules in their os/*.ino files (show card).
    static Hex1bWidget[] BuildHomeTab(WidgetContext<VStackWidget> t, ClientState state, IClusterClient? cc, IDigitalBrain? brain)
    {
        var items = new List<Hex1bWidget> { t.Text("Home: widgets | main | dock — UI surfaces defined in os/*.ino rules + ws layout from Shell") };
        var ws = state.CurrentWorkspace;
        if (ws != null && ws.Regions != null)
        {
            foreach (var r in ws.Regions)
            {
                items.Add(t.Text($"[region {r.Region}]"));
                foreach (var ps in r.Surfaces ?? System.Array.Empty<PlacedSurface>())
                {
                    UiSurface? content = null;
                    foreach (var s in state.AskSurfaces) if (s.SurfaceId == ps.SurfaceId) { content = s; break; }
                    var title = $"pinned {ps.SurfaceId} order{ps.Order}";
                    if (content != null)
                        items.Add(t.Rescue(SurfaceRenderer.Render(t, content.Root, action => state.Fire(action, brain))));
                    else
                        items.Add(t.Text(title + " (content pending)"));
                }
            }
        }
        else
        {
            items.Add(t.Text("(no WorkspaceChanged yet — pin via shell or install to populate)"));
        }
        items.Add(t.Text("dock: (installed launchers — tap emits trigger)"));
        return items.ToArray();
    }

    private sealed class ClientState
    {
        private readonly object _gate = new();
        public bool Connected { get; init; }
        public string? AspireDashboardUrl { get; init; }

        public string ChatInput { get; set; } = "";
        public string CreatorInoContent { get; set; } = "# .ino\n# edit then Pack/Publish";
        public string MarketPeerInput { get; set; } = "";
        public string? LastPeer { get; set; }
        public string? LastPackedId { get; set; }
        public string MarketInstallId { get; set; } = "";

        // Stage 2 identity support: per-user/brain for IDigitalBrain key = "{username}/{brain}"; marketplace global.
        public string Username { get; set; } = "root";
        public string CurrentBrain { get; set; } = "main";
        public string CurrentBrainKey => $"{Username}/{CurrentBrain}";
        private IClusterClient? _cc;
        public void SetClusterClient(IClusterClient? cc) { _cc = cc; }
        public DigitalBrain.Os.Application.IDigitalBrain? GetCurrentBrain()
        {
            if (_cc == null) return null;
            return _cc.GetGrain<DigitalBrain.Os.Application.IDigitalBrain>(CurrentBrainKey);
        }

        private readonly List<string> _chat = new();
        private readonly List<UiSurface> _askSurfaces = new();
        private UiSurface? _marketSurface;
        private readonly List<string> _peerListings = new();
        private readonly List<WorldConnectionInfo> _knownWorlds = new();
        private readonly List<string> _notifs = new() { "ready — use tabs and buttons; Settings for cluster/versions, Ino for aware chat" };

        public IReadOnlyList<string> ChatLines { get { lock (_gate) return _chat.TakeLast(10).ToArray(); } }
        public IReadOnlyList<UiSurface> AskSurfaces { get { lock (_gate) return _askSurfaces.TakeLast(5).ToArray(); } }
        public UiSurface? MarketplaceSurface { get { lock (_gate) return _marketSurface; } }
        public IReadOnlyList<string> PeerListings { get { lock (_gate) return _peerListings.ToArray(); } }
        public IReadOnlyList<WorldConnectionInfo> KnownWorlds { get { lock (_gate) return _knownWorlds.ToArray(); } }
        public IReadOnlyList<string> Notifications { get { lock (_gate) return _notifs.TakeLast(5).ToArray(); } }

        public string GetCurrentClusterLabel(string? localPeer = null)
        {
            if (!string.IsNullOrWhiteSpace(AspireDashboardUrl)) return "aspire-hosted";
            return localPeer ?? "local-sim";
        }

        public void AddChat(bool user, string text)
        {
            lock (_gate) _chat.Add((user ? "you: " : "brain: ") + text);
        }

        public void AddNotification(string msg)
        {
            lock (_gate)
            {
                _notifs.Add(msg);
                if (_notifs.Count > 20) _notifs.RemoveAt(0);
            }
        }

        public void ApplySurface(UiSurface surface)
        {
            lock (_gate)
            {
                var id = surface.SurfaceId;
                // OS5: D5 prefix routing demoted (no longer primary; null placement -> main, layout from WorkspaceChanged.Regions).
                // All surfaces collected; ws drives chrome (widgets/main/dock). Legacy prefix ifs removed as primary router.
                if (id.StartsWith("marketplace", StringComparison.OrdinalIgnoreCase))
                {
                    _marketSurface = surface;
                }
                _askSurfaces.Add(surface);
                AddNotification($"surface: {id} (ws routed)");
            }
        }

        public void ApplyWorld(WorldConnectionInfo w)
        {
            lock (_gate)
            {
                // keep unique by WorldId, latest wins
                _knownWorlds.RemoveAll(x => x.WorldId == w.WorldId);
                _knownWorlds.Add(w);
                if (_knownWorlds.Count > 10) _knownWorlds.RemoveAt(0);
                LastPeer = w.GatewayAddress; // convenient for subsequent peer installs from this world
                AddNotification($"world: {w.WorldId} @ {w.GatewayAddress}");
            }
        }

        public WorkspaceState? CurrentWorkspace { get; private set; }

        public void ApplyWorkspace(WorkspaceChanged w)
        {
            lock (_gate)
            {
                CurrentWorkspace = w.Workspace;
                AddNotification($"workspace updated: {w.Workspace?.Regions?.Length ?? 0} regions (OS2 Home probe)");
            }
        }

        public void SeedFromHistory(IReadOnlyList<Synapse> history)
        {
            lock (_gate)
            {
                _chat.Clear();
                _askSurfaces.Clear();
                _marketSurface = null;
                _knownWorlds.Clear();
                foreach (var s in history)
                {
                    switch (s)
                    {
                        case AgentRequest r: _chat.Add("you: " + r.Prompt); break;
                        case AgentResponse r: _chat.Add("brain: " + r.Content); break;
                        case UiSurface surf: ApplySurface(surf); break;
                        case WorldConnectionInfo w: ApplyWorld(w); break;
                        case WorkspaceChanged wc: ApplyWorkspace(wc); break;
                    }
                }
            }
        }

        public async Task Fire(Synapse action, IDigitalBrain? brain)
        {
            var b = GetCurrentBrain() ?? brain;
            if (b is null) { AddNotification("no brain for tap"); return; }
            AddNotification("tap → " + action.GetType().Name);
            try { await b.SendAsync(action); }
            catch (Exception ex) { AddNotification("tap failed: " + ex.Message); }
        }

        public async Task SendChat(IClusterClient? cc, IDigitalBrain? brain)
        {
            var text = ChatInput.Trim();
            ChatInput = "";
            if (string.IsNullOrWhiteSpace(text)) return;

            var b = GetCurrentBrain() ?? brain;
            if (b is null) return;
            AddChat(true, text);
            try
            {
                await b.SendAsync(new AgentRequest(text));
            }
            catch (Exception ex) { AddNotification("send failed: " + ex.Message); }
        }

        public async Task DoPack(IClusterClient? cc)
        {
            var id = "creator-ino-" + Guid.NewGuid().ToString("N")[..6];
            var (res, producedId) = await ClientActions.PackAsync(cc!, id, CreatorInoContent);
            LastPackedId = producedId;
            AddNotification(res);
        }

        public async Task DoPublish(IDigitalBrain? brain)
        {
            if (LastPackedId is null) { AddNotification("pack first — nothing packed yet in this session"); return; }
            var b = GetCurrentBrain() ?? brain;
            var res = await ClientActions.PublishAsync(b, LastPackedId, null);
            AddNotification(res);
        }

        public async Task DoMarketPeer(IClusterClient? cc)
        {
            var peer = string.IsNullOrWhiteSpace(MarketPeerInput) ? null : MarketPeerInput.Trim();
            var (msg, lines) = await ClientActions.MarketPeerAsync(cc, peer, LastPeer);
            LastPeer = peer ?? LastPeer;
            lock (_gate)
            {
                _peerListings.Clear();
                _peerListings.AddRange(lines);
            }
            AddNotification(msg);
        }

        public async Task DoInstallFromPeerId(IClusterClient? cc, string id)
        {
            if (cc is null) { AddNotification("not connected"); return; }
            if (string.IsNullOrWhiteSpace(LastPeer)) { AddNotification("no last peer set (query a peer first)"); return; }
            var res = await ClientActions.InstallAsync(cc, id, null, LastPeer);
            AddNotification(res);
        }

        public async Task DoInstallIdFromPeer(IClusterClient? cc)
        {
            var id = MarketInstallId.Trim();
            MarketInstallId = "";
            if (string.IsNullOrWhiteSpace(id)) { AddNotification("enter an id to install"); return; }
            if (cc is null) { AddNotification("not connected"); return; }
            var peer = LastPeer;
            if (string.IsNullOrWhiteSpace(peer)) { AddNotification("no peer set — use Query peer or a world from Settings first"); return; }
            var res = await ClientActions.InstallAsync(cc, id, null, peer);
            AddNotification(res);
        }

        public async Task DoInstallIdLocal(IClusterClient? cc)
        {
            var id = MarketInstallId.Trim();
            MarketInstallId = "";
            if (string.IsNullOrWhiteSpace(id)) { AddNotification("enter an id to install"); return; }
            if (cc is null) { AddNotification("not connected"); return; }
            // direct listed (local marketplace state) — the surface buttons do the same via synapse but this is explicit for the "local" case of peer/local
            var marketplace = cc.GetGrain<IMarketplace>(Brain.WellKnownKey);
            try
            {
                var downloaded = await marketplace.InstallListedAsync(id);
                AddNotification($"installed {id} (hash verified: {downloaded.HashVerified})");
            }
            catch (Exception ex) { AddNotification("local install failed: " + ex.Message); }
        }

        public void SetTargetPeerFromWorld(WorldConnectionInfo w)
        {
            LastPeer = w.GatewayAddress;
            MarketPeerInput = w.GatewayAddress;
            AddNotification($"target peer set to {w.WorldId} ({w.GatewayAddress}) — use in Marketplace peer installs or query");
        }

        public async Task DoRefreshCurrentWorld(IDigitalBrain? brain)
        {
            var b = GetCurrentBrain() ?? brain;
            if (b is null) { AddNotification("no brain"); return; }
            try
            {
                var cur = await b.GetCurrentWorldAsync();
                if (cur is not null)
                {
                    ApplyWorld(cur);
                    AddNotification($"current world: {cur.WorldId} {cur.GatewayAddress}");
                }
                else
                {
                    AddNotification("no current world info (env or direct start.cs path)");
                }
            }
            catch (Exception ex) { AddNotification("refresh world failed: " + ex.Message); }
        }

        public async Task DoStartWorld(IDigitalBrain? brain, string worldId)
        {
            var b = GetCurrentBrain() ?? brain;
            if (b is null) { AddNotification("no brain for world start"); return; }
            var launchKey = $"launching:{worldId}";
            AddNotification($"{launchKey} starting (AspireHosted child cluster + dashboard capture)...");
            try
            {
                var info = await b.StartWorldAsync(worldId);
                ApplyWorld(info); // live update via timeline too, but immediate
                var dashNote = string.IsNullOrWhiteSpace(info.DashboardUrl) ? "" : $" dashboard:{ShortenUrl(info.DashboardUrl)}";
                AddNotification($"world {worldId} -> cluster:{info.ClusterId} gw:{info.GatewayAddress}{dashNote}");
                SetTargetPeerFromWorld(info);
            }
            catch (Exception ex) { AddNotification($"start world {worldId} failed: {ex.Message} (check aspire resources/logs)"); }
        }

        // Best-effort: make a real active Aspire dashboard URL available for this (sim) cluster even after plain dotnet run start.cs.
        // Uses `aspire dashboard run` (standalone, detached) + stdout capture (same pattern as DigitalBrainLauncher for real AppHost worlds).
        // Sets AspireDashboardUrl so header + Cluster tab render valid clickable link immediately. Non-blocking.
        // In full aspire AppHost paths the real per-cluster url arrives via WorldConnectionInfo (current cluster).
        public async Task<string?> EnsureActiveAspireDashboardForCurrentClusterAsync()
        {
            if (!string.IsNullOrWhiteSpace(AspireDashboardUrl)) return AspireDashboardUrl;

            try
            {
                // Run without --allow-anonymous so Aspire generates a real browser token and prints the full "Login to the dashboard at http://.../login?t=REALTOKEN" line.
                // Our TryExtractDashboardUrl (the /login?t= prefer path) will capture the whole tokened URL for AspireDashboardUrl / TUI copy.
                var startInfo = new ProcessStartInfo("aspire", "dashboard run --detach --non-interactive --nologo")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(startInfo);
                if (proc is null) return null;

                // Brief drain for the printed dashboard/login url (timeout ~8s). Bounded to avoid CA + hang.
                var captureCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var urlTask = Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < 50 && !captureCts.IsCancellationRequested; i++)
                        {
                            var line = await proc.StandardOutput.ReadLineAsync();
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (DigitalBrainLauncher.TryExtractDashboardUrl(line, out var u) && !string.IsNullOrWhiteSpace(u))
                            {
                                return u;
                            }
                        }
                    }
                    catch { }
                    return null;
                }, captureCts.Token);

                var captured = await urlTask;
                captureCts.Cancel();

                if (!string.IsNullOrWhiteSpace(captured))
                {
                    // mutate (init-only field via reflection for live update; real apps would expose setter or use observable)
                    typeof(ClientState).GetProperty(nameof(AspireDashboardUrl))?.SetValue(this, captured);
                    AddNotification($"aspire dashboard active for cluster: {captured}");
                    return captured;
                }
            }
            catch (Exception ex)
            {
                AddNotification($"dashboard launch skipped: {ex.Message} (install/use aspire cli for full dashboard)");
            }
            return null;
        }

        static string ShortenUrl(string url) => url.Length > 40 ? url[..37] + "..." : url;
    }
}
