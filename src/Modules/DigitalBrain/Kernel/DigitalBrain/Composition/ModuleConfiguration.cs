using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

public sealed class ModuleConfiguration<TModule> where TModule : class, IModule, new()
{
    private readonly ModuleDraft _draft;
    private readonly Action _ensureMutable;
    internal ModuleConfiguration(ModuleDraft draft, Action ensureMutable) { _draft = draft; _ensureMutable = ensureMutable; }

    public void ConfigureLocalServices(Action<IServiceCollection> configure)
    {
        _ensureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _draft.LocalServices.Add(configure);
    }
}

internal abstract class ModuleOptionsSlot
{
    public abstract Type OptionsType { get; }
    public abstract ModuleDefinition Compile();
    public abstract ModuleOptionsSlot Clone();
    public abstract void Edit(Delegate configure);

    public static ModuleOptionsSlot? For(Type moduleType)
    {
        var optionsType = moduleType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IModule<>))
            .Select(i => i.GetGenericArguments()[0]).SingleOrDefault();
        return optionsType is null ? null
            : (ModuleOptionsSlot)Activator.CreateInstance(typeof(ModuleOptionsSlot<,>).MakeGenericType(moduleType, optionsType), [null])!;
    }
}

internal sealed class ModuleOptionsSlot<TModule, TOptions> : ModuleOptionsSlot
    where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
{
    private TOptions _options;
    public ModuleOptionsSlot(TOptions? options) => _options = options ?? new TOptions();
    public override Type OptionsType => typeof(TOptions);
    public override ModuleDefinition Compile() => ModuleOptionsSerialization.Compile<TModule, TOptions>(_options);
    public override ModuleOptionsSlot Clone() => new ModuleOptionsSlot<TModule, TOptions>(RoundTrip(_options));

    public override void Edit(Delegate configure)
    {
        var working = RoundTrip(_options);
        ((Action<TOptions>)configure)(working);
        _options = RoundTrip(working);
    }

    private static TOptions RoundTrip(TOptions options)
        => JsonSerializer.Deserialize<TOptions>(JsonSerializer.Serialize(options))!;
}

internal sealed class ModuleDraft
{
    public Type Type { get; }
    public ModuleOptionsSlot? Options { get; private init; }
    public List<Action<IServiceCollection>> LocalServices { get; } = [];

    public ModuleDraft(Type type)
    {
        Type = type;
        Options = ModuleOptionsSlot.For(type);
    }

    public ModuleDefinition Compile() => Options?.Compile() ?? new(Type);
    public ModuleDraft Copy()
    {
        var copy = new ModuleDraft(Type) { Options = Options?.Clone() };
        copy.LocalServices.AddRange(LocalServices);
        return copy;
    }
}
