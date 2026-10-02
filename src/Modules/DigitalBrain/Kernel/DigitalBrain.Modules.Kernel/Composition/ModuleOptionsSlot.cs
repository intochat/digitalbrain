using System.Text.Json;

namespace DigitalBrain.Core;

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
