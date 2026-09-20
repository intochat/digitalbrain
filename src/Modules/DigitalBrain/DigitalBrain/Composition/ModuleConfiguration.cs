using System.Reflection;

namespace DigitalBrain.Core;

public sealed class ModuleConfiguration<TModule> where TModule : class, IModule, new()
{
    private readonly ModuleDraft _draft;
    private readonly Action _ensureMutable;
    internal ModuleConfiguration(ModuleDraft draft, Action ensureMutable) { _draft = draft; _ensureMutable = ensureMutable; }

    public void ConfigureOptions<TOptions>(Action<TOptions> configure, params string[] assignedMembers) where TOptions : class, new()
    {
        _ensureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        var contract = _draft.RequireContract<TOptions>();
        var copy = (TOptions)contract.Copy(_draft.Options!);
        configure(copy);
        // Validate the explicit-member contract, including assignments equal to defaults.
        _ = contract.WriteOverride(copy, assignedMembers);
        _draft.Options = contract.Copy(copy);
        _draft.Assigned.UnionWith(assignedMembers);
    }

    public void ReplaceOptions<TOptions>(TOptions options) where TOptions : class, new()
    {
        _ensureMutable();
        var contract = _draft.RequireContract<TOptions>();
        // Complete replacement is distinct from a member patch and is validated at compilation.
        _draft.Options = contract.Copy(options);
        _draft.Replace = true;
    }
}

internal sealed class ModuleDraft
{
    public Type Type { get; }
    public IModuleConfigurationContract? Contract { get; }
    public object? Options { get; set; }
    public HashSet<string> Assigned { get; } = new(StringComparer.Ordinal);
    public bool Replace { get; set; }

    public ModuleDraft(Type type)
    {
        Type = type;
        if (type.GetCustomAttribute<ModuleConfigurationAttribute>() is { } attribute)
        {
            Contract = Activator.CreateInstance(attribute.ContractType) as IModuleConfigurationContract
                ?? throw new ArgumentException($"Invalid configuration contract for {type.Name}.");
            if (Contract.ModuleType != type) { throw new ArgumentException($"Configuration contract belongs to another module: {type.Name}."); }
            Options = Contract.CreateDefaults();
        }
    }

    public IModuleConfigurationContract RequireContract<TOptions>() => Contract is { } contract && contract.OptionsType == typeof(TOptions)
        ? contract : throw new ArgumentException($"{Type.Name} does not accept {typeof(TOptions).Name}.");
    public ModuleDefinition Compile() => Contract?.Compile(Options!) ?? new(Type);
    public ModuleDraft Copy()
    {
        var copy = new ModuleDraft(Type) { Options = Contract?.Copy(Options!), Replace = Replace };
        copy.Assigned.UnionWith(Assigned);
        return copy;
    }
}
