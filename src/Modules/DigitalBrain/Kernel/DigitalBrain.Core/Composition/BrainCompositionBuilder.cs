namespace DigitalBrain.Core;

public sealed class BrainCompositionBuilder
{
    private readonly Dictionary<Type, ModuleDraft> _modules = [];
    private BrainComposition? _built;

    public BrainCompositionBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        Declare<TModule>(out var draft);
        configure?.Invoke(new(draft, EnsureMutable));
        return this;
    }

    public BrainCompositionBuilder WithModule<TModule, TOptions>(Action<TOptions>? configureOptions = null,
        Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        Declare<TModule>(out var draft);
        if (configureOptions is not null) { draft.Options!.Edit(configureOptions); }
        configure?.Invoke(new(draft, EnsureMutable));
        return this;
    }

    // Adds missing module requirements; explicitly declared modules keep their configuration.
    public BrainCompositionBuilder RequireModules(IEnumerable<Type> modules)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var module in modules.Where(module => !_modules.ContainsKey(module)))
        { _modules.Add(module, new ModuleDraft(module)); }
        return this;
    }

    public BrainCompositionBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        var copy = RequireDeclared<TModule>().Copy();
        configure(new(copy, EnsureMutable));
        _modules[typeof(TModule)] = copy;
        return this;
    }

    public BrainCompositionBuilder ConfigureModule<TModule, TOptions>(Action<TOptions> configureOptions)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configureOptions);
        var copy = RequireDeclared<TModule>().Copy();
        copy.Options!.Edit(configureOptions);
        _modules[typeof(TModule)] = copy;
        return this;
    }

    public BrainComposition Build()
    {
        if (_built is not null) { return _built; }
        var modules = ModuleComposition.Resolve(_modules.Values.Select(m => m.Compile()).ToArray());
        ModuleSettingsValidation.ValidatePublicSettings(modules);
        return _built = new(modules, _modules.Values.SelectMany(m => m.LocalServices).ToArray());
    }

    public BrainCompositionBuilder ApplyOverrides(string token)
    {
        EnsureMutable();
        var edits = CompositionOverrideTransport.Take(token);
        var replacements = new Dictionary<Type, ModuleDraft>();
        var omitted = new HashSet<Type>();
        foreach (var edit in edits)
        {
            var existing = _modules.TryGetValue(edit.ModuleType, out var declared) ? declared
                : throw new ArgumentException("An override targets a module not declared by the application.", nameof(token));
            if (edit.Omit) { omitted.Add(edit.ModuleType); continue; }
            var draft = existing.Copy();
            foreach (var configure in edit.OptionEdits)
            {
                (draft.Options ?? throw new ArgumentException("This module has no configurable options.", nameof(token))).Edit(configure);
            }
            _ = draft.Compile();
            replacements.Add(draft.Type, draft);
        }
        // An invalid override never leaves a partially overridden application.
        foreach (var type in omitted) { _modules.Remove(type); }
        foreach (var (type, draft) in replacements) { _modules[type] = draft; }
        return this;
    }

    private void Declare<TModule>(out ModuleDraft draft) where TModule : class, IModule, new()
    {
        EnsureMutable();
        if (_modules.ContainsKey(typeof(TModule)))
        { throw new InvalidOperationException($"{typeof(TModule).Name} is already declared. Use ConfigureModule to change it."); }
        draft = new ModuleDraft(typeof(TModule));
        _modules.Add(typeof(TModule), draft);
    }

    private ModuleDraft RequireDeclared<TModule>()
        => _modules.TryGetValue(typeof(TModule), out var draft) ? draft
            : throw new InvalidOperationException($"{typeof(TModule).Name} is not declared in this application.");

    private void EnsureMutable()
    {
        if (_built is not null) { throw new InvalidOperationException("The composition is frozen; create a new builder to change it."); }
    }
}
