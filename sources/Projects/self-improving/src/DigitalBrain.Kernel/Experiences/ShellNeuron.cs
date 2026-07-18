using DigitalBrain.Protocol;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;

namespace DigitalBrain.Kernel.Experiences;

[GrainType("shell")]
public sealed class ShellNeuron : Neuron, IUiNeuron,
    IHandle<PinSurface>, IHandle<UnpinSurface>, IHandle<MoveSurface>,
    IHandle<BundleInstalled>, IHandle<BundleUninstalled>, IHandle<UiSurface>,
    IHandle<OpenWindow>, IHandle<CloseWindow>, IHandle<MoveResizeWindow>,
    IHandle<RaiseWindow>, IHandle<DockWindow>, IHandle<AutoLayoutWindows>
{
    private readonly IPersistentState<WorkspaceState> _state;
    private readonly Dictionary<string, UiWidget> _recentSurfaces = new();


    public ShellNeuron(
        [PersistentState("workspace", "Default")] IPersistentState<WorkspaceState> state)
    {
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (string.IsNullOrEmpty(_state.State.Username))
        {
            _state.State.Username = this.GetPrimaryKeyString();
        }
        await _state.WriteStateAsync(cancellationToken);

        // SeedInitialContentSurfaces removed per single-source: initial content surfaces arrive via .ino rules (shell.ino on UiSurface + bundle show cards on install via RuleHost).
        // _recentSurfaces now populated only by runtime arrivals from rule-produced UiSurface.
        await Emit(new UiSurface("shell-bootstrap", Self, new Text("bootstrap")));
        var graphGrain = GrainFactory.GetGrain<IBrainGraphNeuron>("global");
        await graphGrain.PingAsync(cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public Task<UiState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var ui = new UiState { Username = _state.State.Username };
        return Task.FromResult(ui);
    }

    public async Task RegisterTelemetryAsync(string evt, Dictionary<string, string> data, CancellationToken cancellationToken = default)
    {
        await Emit(new NeuronTelemetry(Self, evt, data));
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task SwitchBrainAsync(string brainId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(brainId)) return;
        await RegisterTelemetryAsync("UiBrainSwitched", new Dictionary<string, string> { ["brainId"] = brainId }, cancellationToken);
    }

    public async Task AddBrainAsync(string brainId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(brainId)) return;
        await RegisterTelemetryAsync("UiBrainAdded", new Dictionary<string, string> { ["brainId"] = brainId }, cancellationToken);
    }

    public Task HandleAsync(NeuronTelemetry telemetry, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task HandleAsync(PinSurface pinSurface, CancellationToken cancellationToken)
    {
        await ApplyPlacement(pinSurface.SurfaceId, pinSurface.Region, pinSurface.Order, pinned: true, pinSurface.SurfaceId, cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task HandleAsync(UnpinSurface unpinSurface, CancellationToken cancellationToken)
    {
        await RemovePlacement(unpinSurface.SurfaceId, cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task HandleAsync(MoveSurface moveSurface, CancellationToken cancellationToken)
    {
        await ApplyPlacement(moveSurface.SurfaceId, moveSurface.Region, moveSurface.Order, pinned: true, moveSurface.SurfaceId, cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task HandleAsync(BundleInstalled bundleInstalled, CancellationToken cancellationToken)
    {
        var id = bundleInstalled.BundleId.Value;
        if (id == "kernel-tasks" || id.Contains("tasks"))
        {
            await ApplyPlacement("kerneltasks", "widgets", 1, true, id, cancellationToken);
            await OpenWindowInternal("kerneltasks", "Active Tasks", 50, 50, 350, 450, cancellationToken);
        }
        else if (id == "weather-watcher" || id.Contains("weather"))
        {
            await ApplyPlacement("weather", "widgets", 2, true, id, cancellationToken);
            await OpenWindowInternal("weather", "Weather", 420, 50, 320, 400, cancellationToken);
        }
        else if (id == "gmail-last-senders" || id.Contains("gmail"))
        {
            await ApplyPlacement("gmail-senders-root", "widgets", 3, true, id, cancellationToken);
            await OpenWindowInternal("ui-def-gmail-last-senders", "Gmail", 50, 400, 380, 400, cancellationToken);
        }
        else if (id == "marketplace")
        {
            await ApplyPlacement(id, "main", 0, false, id, cancellationToken);
            await OpenWindowInternal(id, "Marketplace", 100, 100, 400, 500, cancellationToken);
        }
        else if (id == "creator" || id == "llm-agent")
        {
            await ApplyPlacement("ui-def-creator", "main", 10, false, id, cancellationToken);
            await OpenWindowInternal("ui-def-creator", "Creator Workspace", 200, 150, 420, 550, cancellationToken);
        }
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task HandleAsync(BundleUninstalled bundleUninstalled, CancellationToken cancellationToken)
    {
        await RemovePlacementForBundle(bundleUninstalled.BundleId, cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task HandleAsync(UiSurface surface, CancellationToken cancellationToken)
    {
        _recentSurfaces[surface.SurfaceId] = surface.Root;
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    private async Task ApplyPlacement(string surfaceId, string region, int order, bool pinned, string owner, CancellationToken cancellationToken)
    {
        var regions = _state.State.Regions?.ToList() ?? new List<RegionPlacement>();
        var regionPlacement = regions.FirstOrDefault(r => r.Region == region);
        if (regionPlacement == null)
        {
            regionPlacement = new RegionPlacement(region, Array.Empty<PlacedSurface>());
            regions.Add(regionPlacement);
        }
        var surfacesList = regionPlacement.Surfaces?.ToList() ?? new List<PlacedSurface>();
        surfacesList.RemoveAll(ps => ps.SurfaceId == surfaceId);
        surfacesList.Add(new PlacedSurface(surfaceId, owner ?? surfaceId, order, pinned));
        surfacesList = surfacesList.OrderBy(ps => ps.Order).ThenBy(ps => ps.SurfaceId).ToList();
        var updated = regions
            .Select(r => r.Region == region ? new RegionPlacement(region, surfacesList.ToArray()) : r)
            .ToArray();
        _state.State.Regions = updated;
        await _state.WriteStateAsync(cancellationToken);
    }

    private async Task RemovePlacement(string surfaceId, CancellationToken cancellationToken)
    {
        var regions = _state.State.Regions?.ToList() ?? new List<RegionPlacement>();
        bool changed = false;
        for (int i = 0; i < regions.Count; i++)
        {
            var regionPlacement = regions[i];
            var filtered = regionPlacement.Surfaces?.Where(ps => ps.SurfaceId != surfaceId).ToArray() ?? Array.Empty<PlacedSurface>();
            if (filtered.Length != (regionPlacement.Surfaces?.Length ?? 0))
            {
                regions[i] = new RegionPlacement(regionPlacement.Region, filtered);
                changed = true;
            }
        }
        if (changed)
        {
            _state.State.Regions = regions.ToArray();
            await _state.WriteStateAsync(cancellationToken);
        }
    }

    private async Task RemovePlacementForBundle(string bundleId, CancellationToken cancellationToken)
    {
        var regions = _state.State.Regions?.ToList() ?? new List<RegionPlacement>();
        bool changed = false;
        for (int i = 0; i < regions.Count; i++)
        {
            var regionPlacement = regions[i];
            var filtered = regionPlacement.Surfaces?.Where(ps => ps.OwnerBundleId != bundleId && ps.SurfaceId != bundleId).ToArray() ?? Array.Empty<PlacedSurface>();
            if (filtered.Length != (regionPlacement.Surfaces?.Length ?? 0))
            {
                regions[i] = new RegionPlacement(regionPlacement.Region, filtered);
                changed = true;
            }
        }
        if (changed)
        {
            _state.State.Regions = regions.ToArray();
            await _state.WriteStateAsync(cancellationToken);
        }
    }

    private async Task EmitCurrentWorkspaceAsync(CancellationToken cancellationToken)
    {
        await Emit(new WorkspaceChanged(_state.State));

        // Prefer full declarative OS chrome (nav, header, main+widgets regions via [[ ]] markers) produced by shell.ino rule (ui-def-shell surface). ResolveFrame swaps markers for placed content from other .ino bundles. Thin shell: state only.
        UiWidget chrome = _recentSurfaces.TryGetValue("ui-def-shell", out var shellTemplateFromIno)
            ? shellTemplateFromIno
            : new Column(new UiWidget[] { new Text("shell chrome loading from .ino...") });

        var resolved = ResolveFrame(chrome);
        var uiShell = new UiSurface("ui-shell", Self, resolved);
        await Emit(uiShell);
        SurfaceStreamService.Publish(SurfaceStreamService.ToMessage(uiShell));

        var windowWidgets = new List<UiWidget>();
        var windows = _state.State.Windows ?? Array.Empty<WindowState>();
        foreach (var win in windows.OrderBy(w => w.ZIndex))
        {
            if (win.State == "floating")
            {
                var content = _recentSurfaces.TryGetValue(win.SurfaceId, out var widget)
                    ? widget
                    : new Text($"Content for {win.SurfaceId} loading...");
                windowWidgets.Add(new WindowFrame(win.Title, content, win.WindowId, win.X, win.Y, win.Width, win.Height, win.ZIndex, win.State));
            }
        }
        var uiWindows = new UiSurface("ui-windows", Self, new Column(windowWidgets.ToArray()));
        await Emit(uiWindows);
        SurfaceStreamService.Publish(SurfaceStreamService.ToMessage(uiWindows));
    }

    public async Task HandleAsync(OpenWindow openWindow, CancellationToken cancellationToken)
    {
        await OpenWindowInternal(openWindow.SurfaceId, openWindow.Title, openWindow.X, openWindow.Y, openWindow.Width, openWindow.Height, cancellationToken);
    }

    private async Task OpenWindowInternal(string surfaceId, string title, double x, double y, double width, double height, CancellationToken cancellationToken)
    {
        var windows = _state.State.Windows?.ToList() ?? new List<WindowState>();
        var winId = $"win-{surfaceId}";
        windows.RemoveAll(w => w.WindowId == winId);
        int maxZ = windows.Count > 0 ? windows.Max(w => w.ZIndex) : 0;
        windows.Add(new WindowState(winId, surfaceId, title, x, y, width, height, maxZ + 1, "floating"));
        _state.State.Windows = windows.ToArray();
        await _state.WriteStateAsync(cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task HandleAsync(CloseWindow closeWindow, CancellationToken cancellationToken)
    {
        var windows = _state.State.Windows?.ToList() ?? new List<WindowState>();
        windows.RemoveAll(w => w.WindowId == closeWindow.WindowId);
        _state.State.Windows = windows.ToArray();
        await _state.WriteStateAsync(cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    public async Task HandleAsync(MoveResizeWindow moveResize, CancellationToken cancellationToken)
    {
        var windows = _state.State.Windows?.ToList() ?? new List<WindowState>();
        var win = windows.FirstOrDefault(w => w.WindowId == moveResize.WindowId);
        if (win != null)
        {
            windows.Remove(win);
            windows.Add(win with { X = moveResize.X, Y = moveResize.Y, Width = moveResize.Width, Height = moveResize.Height });
            _state.State.Windows = windows.ToArray();
            await _state.WriteStateAsync(cancellationToken);
            await EmitCurrentWorkspaceAsync(cancellationToken);
        }
    }

    public async Task HandleAsync(RaiseWindow raiseWindow, CancellationToken cancellationToken)
    {
        var windows = _state.State.Windows?.ToList() ?? new List<WindowState>();
        var win = windows.FirstOrDefault(w => w.WindowId == raiseWindow.WindowId);
        if (win != null)
        {
            int maxZ = windows.Count > 0 ? windows.Max(w => w.ZIndex) : 0;
            windows.Remove(win);
            windows.Add(win with { ZIndex = maxZ + 1 });
            _state.State.Windows = windows.ToArray();
            await _state.WriteStateAsync(cancellationToken);
            await EmitCurrentWorkspaceAsync(cancellationToken);
        }
    }

    public async Task HandleAsync(DockWindow dockWindow, CancellationToken cancellationToken)
    {
        var windows = _state.State.Windows?.ToList() ?? new List<WindowState>();
        var win = windows.FirstOrDefault(w => w.WindowId == dockWindow.WindowId);
        if (win != null)
        {
            windows.Remove(win);
            windows.Add(win with { State = "docked" });
            _state.State.Windows = windows.ToArray();
            await ApplyPlacement(win.SurfaceId, dockWindow.Region, 0, true, win.SurfaceId, cancellationToken);
            await EmitCurrentWorkspaceAsync(cancellationToken);
        }
    }

    public async Task HandleAsync(AutoLayoutWindows autoLayout, CancellationToken cancellationToken)
    {
        var windows = _state.State.Windows?.ToList() ?? new List<WindowState>();
        double startX = 20;
        double startY = 20;
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (w.State == "floating")
            {
                windows[i] = w with { X = startX + (i * 40), Y = startY + (i * 30), Width = 320, Height = 400 };
            }
        }
        _state.State.Windows = windows.ToArray();
        await _state.WriteStateAsync(cancellationToken);
        await EmitCurrentWorkspaceAsync(cancellationToken);
    }

    private UiWidget ResolveFrame(UiWidget widget)
    {
        return widget switch
        {
            Text t when IsRegionMarker(t.Value) => BuildRegionContent(ExtractRegionName(t.Value)),
            Column c => new Column(c.Children.Select(ResolveFrame).ToArray()),
            Row r => new Row(r.Children.Select(ResolveFrame).ToArray()),
            Card c => new Card(c.Title, ResolveFrame(c.Body)),
            MainPane mp => new MainPane(ResolveFrame(mp.Content)),
            _ => widget
        };
    }

    private bool IsRegionMarker(string value) => !string.IsNullOrEmpty(value) && value.TrimStart().StartsWith("[[region:");

    private string ExtractRegionName(string value)
    {
        var s = value.Trim();
        var start = s.IndexOf("[[region:") + "[[region:".Length;
        var end = s.IndexOf("]]", start);
        return end > start ? s.Substring(start, end - start).Trim() : "main";
    }

    private UiWidget BuildRegionContent(string region)
    {
        var regions = _state.State.Regions;
        if (regions == null) return new Text($"({region} empty)");
        var regionPlacement = regions.FirstOrDefault(r => r.Region == region);
        if (regionPlacement?.Surfaces == null || regionPlacement.Surfaces.Length == 0) return new Text($"({region} empty)");
        var kids = new List<UiWidget>();
        foreach (var placedSurface in regionPlacement.Surfaces.OrderBy(p => p.Order).ThenBy(p => p.SurfaceId))
        {
            if (_recentSurfaces.TryGetValue(placedSurface.SurfaceId, out var widget))
                kids.Add(widget);
            else
                kids.Add(new Text(placedSurface.SurfaceId));
        }
        return kids.Count == 1 ? kids[0] : new Column(kids.ToArray());
    }

    public Task PinForTest(PinSurface pinSurface, CancellationToken cancellationToken = default) => HandleAsync(pinSurface, cancellationToken);
    public WorkspaceState GetWorkspaceForTest() => _state.State;
}