namespace DigitalBrain.Kernel;

public sealed class BrainCompositionBuilder
{
    private readonly Dictionary<Type, ModuleDraft> _modules = [];

    public BrainCompositionBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        Declare<TModule>(out var draft);
        configure?.Invoke(new(draft));
        return this;
    }

    public BrainCompositionBuilder WithModule<TModule, TOptions>(Action<TOptions>? configureOptions = null,
        Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        Declare<TModule>(out var draft);
        if (configureOptions is not null) { draft.Options!.Edit(configureOptions); }
        configure?.Invoke(new(draft));
        return this;
    }

    // Adds missing module requirements; explicitly declared modules keep their configuration.
    public BrainCompositionBuilder RequireModules(IEnumerable<Type> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var module in modules.Where(module => !_modules.ContainsKey(module)))
        { _modules.Add(module, new ModuleDraft(module)); }
        return this;
    }

    public BrainCompositionBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new(RequireDeclared<TModule>()));
        return this;
    }

    public BrainCompositionBuilder ConfigureModule<TModule, TOptions>(Action<TOptions> configureOptions)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        ArgumentNullException.ThrowIfNull(configureOptions);
        RequireDeclared<TModule>().Options!.Edit(configureOptions);
        return this;
    }

    public BrainComposition Build()
    {
        var modules = ModuleComposition.Resolve(_modules.Values.Select(m => m.Compile()).ToArray());
        ModuleSettingsValidation.ValidatePublicSettings(modules);
        return new(modules, _modules.Values.SelectMany(m => m.LocalServices).ToArray());
    }

    private void Declare<TModule>(out ModuleDraft draft) where TModule : class, IModule, new()
    {
        if (_modules.ContainsKey(typeof(TModule)))
        { throw new InvalidOperationException($"{typeof(TModule).Name} is already declared. Use ConfigureModule to change it."); }
        draft = new ModuleDraft(typeof(TModule));
        _modules.Add(typeof(TModule), draft);
    }

    private ModuleDraft RequireDeclared<TModule>()
        => _modules.TryGetValue(typeof(TModule), out var draft) ? draft
            : throw new InvalidOperationException($"{typeof(TModule).Name} is not declared in this application.");
}
