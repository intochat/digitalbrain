using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Apps.Hosting;
using DigitalBrain.UI;

namespace DigitalBrain.Runtime.Apps;

public sealed class InProcessAppHost : IAppHost
{
    private readonly Func<Type, string, object> _neuronResolver;
    private readonly Func<Type, object> _capabilityResolver;
    private readonly IPresentationSink _sink;
    private readonly Func<Type, object?>? _services;
    private readonly ConcurrentDictionary<string, HostedProgram> _programs = new(StringComparer.Ordinal);

    public InProcessAppHost(
        Func<Type, string, object> neuronResolver,
        Func<Type, object> capabilityResolver,
        IPresentationSink sink,
        object? logger = null,
        Func<Type, object?>? services = null)
    {
        ArgumentNullException.ThrowIfNull(neuronResolver);
        ArgumentNullException.ThrowIfNull(capabilityResolver);
        ArgumentNullException.ThrowIfNull(sink);
        _neuronResolver = neuronResolver;
        _capabilityResolver = capabilityResolver;
        _sink = sink;
        _ = logger;
        _services = services;
    }

    public Task Assign(ProgramAssignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignment.Owner);
        ct.ThrowIfCancellationRequested();

        if (_programs.TryRemove(assignment.Owner, out var previous))
        {
            previous.Unload();
        }

        if (assignment.Behaviors.Count == 0)
        {
            return Task.CompletedTask;
        }

        return LoadAndPresentPrebuiltAsync(assignment, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task LoadAndPresentPrebuiltAsync(ProgramAssignment assignment, CancellationToken ct)
    {
        var hosted = LoadAndStartPrebuilt(assignment);
        _programs[assignment.Owner] = hosted;
        return PresentCurrentAsync(hosted, ct);
    }

    public async Task AssignType(string owner, Type appType, IReadOnlyDictionary<string, string> settings, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(appType);
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        if (_programs.TryRemove(owner, out var previous))
        {
            previous.Unload();
        }

        var definition = new AppDefinition(appType);
        HostedProgram? hosted = null;
        var app = CreateApp(definition, () => hosted!);
        var turns = new AppTurns(app, definition, token => PresentCurrentAsync(hosted!, token));
        var hash = appType.Assembly.GetName().Version?.ToString() ?? "compiled";
        var behaviors = new[] { new BehaviorView(BehaviorIds.For(owner, ""), hash, "Running", null) };
        hosted = new HostedProgram(owner, hash, turns, settings, context: null, behaviors);
        _programs[owner] = hosted;
        await PresentCurrentAsync(hosted, ct).ConfigureAwait(false);
    }

    public Task Retire(string owner, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ct.ThrowIfCancellationRequested();
        UnloadProgram(owner);
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UnloadProgram(string owner)
    {
        if (_programs.TryRemove(owner, out var removed))
        {
            removed.Unload();
        }
    }

    public Task<BehaviorView[]> Describe(string owner, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_programs.TryGetValue(owner, out var hosted) ? hosted.Behaviors : []);
    }

    public Task Request(string owner, string operation, object? argument = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!_programs.TryGetValue(owner, out var hosted))
        {
            throw new InvalidOperationException($"No program is hosted for owner '{owner}'.");
        }
        return hosted.Turns.Request(operation, argument, ct);
    }

    public AppTurns? TryGetTurns(string owner)
        => _programs.TryGetValue(owner, out var hosted) ? hosted.Turns : null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private HostedProgram LoadAndStartPrebuilt(ProgramAssignment assignment)
    {
        var context = new AppLoadContext(assignment.Owner);
        try
        {
            Type? appType = null;
            var behaviors = new List<BehaviorView>(assignment.Behaviors.Count);
            foreach (var (path, prebuilt) in assignment.Behaviors)
            {
                using var image = new MemoryStream(prebuilt.Assembly);
                using var symbols = new MemoryStream(prebuilt.Symbols);
                var assembly = prebuilt.Symbols.Length == 0
                    ? context.LoadFromStream(image)
                    : context.LoadFromStream(image, symbols);
                var found = assembly.GetTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsDefined(typeof(AppAttribute), inherit: false))
                    .ToArray();
                if (found.Length == 1)
                {
                    appType = found[0];
                }
                behaviors.Add(new BehaviorView(BehaviorIds.For(assignment.Owner, path), prebuilt.Hash, "Running", null));
            }

            if (appType is null)
            {
                throw new InvalidOperationException($"Program '{assignment.Owner}' does not declare an [App] class.");
            }

            var definition = new AppDefinition(appType);
            HostedProgram? hosted = null;
            var app = CreateApp(definition, () => hosted!);
            var turns = new AppTurns(app, definition, token => PresentCurrentAsync(hosted!, token));
            hosted = new HostedProgram(assignment.Owner, assignment.Hash, turns, assignment.Settings, context, [.. behaviors]);
            return hosted;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private App CreateApp(AppDefinition definition, Func<IPresenter> presenter)
    {
        var instance = definition.Create(serviceType =>
        {
            if (serviceType == typeof(IPresenter))
            {
                return presenter();
            }
            if (_services?.Invoke(serviceType) is { } resolved)
            {
                return resolved;
            }
            if (serviceType.IsInterface && serviceType.IsDefined(typeof(CapabilityAttribute), inherit: false))
            {
                return _capabilityResolver(serviceType);
            }
            return null;
        });
        instance.AttachNeuronResolver(_neuronResolver);
        instance.AttachCapabilityResolver(_capabilityResolver);
        return instance;
    }

    private Task PresentCurrentAsync(HostedProgram hosted, CancellationToken ct)
    {
        var type = hosted.Turns.App.GetType();
        var (presentedSide, presentedMain) = hosted.ConsumePresented();
        var side = ReadProperty(type, hosted.Turns.App, "Side")
            ?? InvokeRender(type, hosted.Turns.App, "RenderSide", "Side")
            ?? presentedSide;
        var main = ReadProperty(type, hosted.Turns.App, "Main")
            ?? InvokeRender(type, hosted.Turns.App, "Render", hosted.Turns.Definition.Name)
            ?? presentedMain;
        return side is not null || main is not null
            ? _sink.Present(hosted.Owner, side, main, ct)
            : Task.CompletedTask;
    }

    private static View? ReadProperty(Type type, App instance, string name)
    {
        var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (prop is null || !(typeof(View).IsAssignableFrom(prop.PropertyType) || typeof(Node).IsAssignableFrom(prop.PropertyType)))
        {
            return null;
        }
        var value = prop.GetValue(instance);
        return value as View ?? (value is Node node ? new View(name, node) : null);
    }

    private static View? InvokeRender(Type type, App instance, string methodName, string fallbackTitle)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
        if (method is null || !(typeof(View).IsAssignableFrom(method.ReturnType) || typeof(Node).IsAssignableFrom(method.ReturnType)))
        {
            return null;
        }
        var value = method.Invoke(instance, null);
        return value as View ?? (value is Node node ? new View(fallbackTitle, node) : null);
    }
}
