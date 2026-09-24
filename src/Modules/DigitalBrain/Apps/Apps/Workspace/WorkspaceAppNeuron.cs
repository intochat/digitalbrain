using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.runtime-state")]
public sealed record AppRuntimeState
{
    [Id(0)] public AppSnapshot? Current { get; init; }
    [Id(1)] public Dictionary<Guid, AppDispatchReceipt> Operations { get; init; } = [];
    [Id(2)] public long BrainGeneration { get; init; }
    [Id(3)] public AppDispatchReceipt? PendingReceipt { get; init; }
}

[GenerateSerializer, Alias("apps.dispatch-receipt")]
public sealed record AppDispatchReceipt([property: Id(0)] AppDispatch Request, [property: Id(1)] AppSnapshot Result);

[GrainType("apps.workspace-app")]
public class WorkspaceAppNeuron(
    [PersistentState("app", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppRuntimeState> store,
    AppBehaviorRegistry behaviors, IConfiguration hostConfiguration)
    : App<AppRuntimeState>(store), IWorkspaceApp
{
    public Task<AppSnapshot> Read() => Task.FromResult(Current);
    private AppSnapshot Current => Snapshot.Current ?? throw new KeyNotFoundException("The app is not installed.");

    public async Task<AppSnapshot> Install(AppManifest manifest, IReadOnlyDictionary<string, string> configuration)
    {
        ManifestValidator.Validate(manifest);
        var graph = manifest.Composition ?? throw new AppManifestException("An executable app requires a composition.");
        behaviors.Validate(graph);
        var available = hostConfiguration.GetSection("DigitalBrain:Modules").Get<string[]>() ?? [];
        foreach (var module in graph.RequiredModules)
        {
            if (module != typeof(AppsModule).FullName && !available.Any(type => type.Split(',')[0] == module))
            { throw new AppManifestException($"Required module '{module}' is not available on this host."); }
        }
        var config = Configuration(graph, configuration);
        if (Snapshot.Current is { } existing)
        {
            if (AppManifestJson.Serialize(existing.Manifest) != AppManifestJson.Serialize(manifest))
            { throw new InvalidOperationException("An installed app cannot be replaced implicitly. Install a distinct version through an explicit upgrade."); }
            return existing;
        }
        var installed = new AppSnapshot { Manifest = manifest, Configuration = config, Revision = 1 };
        await Persist(installed);
        return graph.Activation == AppActivation.Background ? await Activate() : installed;
    }

    public async Task<AppSnapshot> Configure(IReadOnlyDictionary<string, string> configuration)
    {
        var current = Current;
        AppCompositionValidation.ValidateMap(configuration);
        var next = new Dictionary<string, string>(current.Configuration);
        foreach (var pair in configuration) { next[pair.Key] = pair.Value; }
        var merged = Configuration(current.Manifest.Composition!, next);
        return await Persist(current with { Configuration = merged, Revision = current.Revision + 1 });
    }

    public async Task<AppSnapshot> Activate()
    {
        var current = Current;
        if (current.Status == AppStatus.Running) { return current; }
        behaviors.Validate(current.Manifest.Composition!);
        var active = current with { Status = AppStatus.Running, Revision = current.Revision + 1 };
        // All bindings are resolved before activation. State and activation effects are committed together.
        active = Execute(active, "activated", "", out var signals);
        await Persist(active);
        foreach (var signal in signals) { await PublishAsync(signal); }
        await PublishAsync(new AppSignal("$app", "activated", ""));
        return active;
    }

    public Task<AppSnapshot> Deactivate() => Current.Status == AppStatus.Stopped ? Read()
        : Persist(Current with { Status = AppStatus.Stopped, Revision = Current.Revision + 1 });

    public async Task<AppSnapshot> WorkspaceActivated(long generation)
    {
        if (generation <= 0) { throw new ArgumentOutOfRangeException(nameof(generation)); }
        if (Snapshot.BrainGeneration >= generation || Current.Status == AppStatus.Stopped) { return Current; }
        await Activate();
        var next = Execute(Current with { Revision = Current.Revision + 1 }, "activated", "", out var signals, "$brain");
        await Save(Snapshot with { Current = next, BrainGeneration = generation }, new AppChanged(next.Revision, next.Status));
        foreach (var signal in signals) { await PublishAsync(signal); }
        return next;
    }

    public async Task<AppSnapshot> Dispatch(AppDispatch request)
    {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Signal) || request.Value is null || request.Value.Length > 16384)
        { throw new ArgumentException("A unique operation id, signal and value of at most 16384 characters are required."); }
        // Commit the previous receipt before accepting another operation. If a crash happens in
        // between, the root still contains it and Store is idempotent: no result can be lost.
        if (Snapshot.PendingReceipt is { } pending)
        { await Receipt(pending.Request.OperationId).Store(pending); }
        var prior = Snapshot.Operations.GetValueOrDefault(request.OperationId) ?? await Receipt(request.OperationId).Read();
        if (prior is not null)
        {
            if (prior.Request != request) { throw new InvalidOperationException("The operation id belongs to another request."); }
            return prior.Result;
        }
        if (Current.Status == AppStatus.Stopped) { throw new InvalidOperationException("Activate the app before running it."); }
        if (!Current.Manifest.Operations.Any(x => x.Name == request.Signal)) { throw new ArgumentException("The app does not export this operation."); }
        if (Current.Status != AppStatus.Running) { await Activate(); }
        var next = Execute(Current with { Revision = Current.Revision + 1 }, request.Signal, request.Value, out var signals);
        await Save(Snapshot with { Current = next, PendingReceipt = new(request, next) }, new AppChanged(next.Revision, next.Status));
        foreach (var signal in signals) { await PublishAsync(signal); }
        return next;
    }

    private IAppOperationReceipt Receipt(Guid operation) => GrainFactory.GetGrain<IAppOperationReceipt>(this.GetPrimaryKeyString() + "/operations/" + operation.ToString("N"));

    private AppSnapshot Execute(AppSnapshot current, string signal, string value, out List<AppSignal> signals, string source = "$app")
    {
        signals = [];
        var graph = current.Manifest.Composition!;
        var parts = graph.Parts.ToDictionary(x => x.Name);
        var values = new Dictionary<string, string>(current.Values);
        var pending = new Queue<AppSignal>();
        pending.Enqueue(new(source, signal, value));
        var output = current.Output;
        var deliveries = 0;
        while (pending.TryDequeue(out var input))
        {
            foreach (var edge in graph.Bindings.Where(edge => edge.Source == input.Source && edge.Signal == input.Name))
            {
                if (++deliveries > 256) { throw new AppManifestException("A signal may invoke at most 256 behavior parts."); }
                var part = parts[edge.Target];
                output = behaviors.Get(part.Behavior).Execute(input.Value, part, current.Configuration);
                if (output.Length > 16384) { throw new AppManifestException("Behavior output exceeds 16384 characters."); }
                values[part.Name] = output;
                var completed = new AppSignal(part.Name, "completed", output);
                signals.Add(completed);
                pending.Enqueue(completed);
            }
        }
        return current with { Values = values, Output = output };
    }

    private async Task<AppSnapshot> Persist(AppSnapshot next)
    {
        await Save(Snapshot with { Current = next }, new AppChanged(next.Revision, next.Status));
        return next;
    }

    private static Dictionary<string, string> Configuration(AppComposition graph, IReadOnlyDictionary<string, string> supplied)
    {
        AppCompositionValidation.ValidateMap(supplied);
        var result = new Dictionary<string, string>(graph.Defaults);
        foreach (var pair in supplied)
        {
            if (!result.ContainsKey(pair.Key)) { throw new AppManifestException($"Configuration field '{pair.Key}' is not declared by the app."); }
            result[pair.Key] = pair.Value;
        }
        return result;
    }
}
