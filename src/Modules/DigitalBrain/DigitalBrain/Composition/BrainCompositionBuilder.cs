namespace DigitalBrain.Core;

public sealed class BrainCompositionBuilder
{
    private readonly Dictionary<Type, ModuleDraft> _modules = [];
    private BrainComposition? _built;

    public BrainCompositionBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        if (_modules.ContainsKey(typeof(TModule)))
            { throw new InvalidOperationException($"{typeof(TModule).Name} is already declared. Use ConfigureModule to change it."); }
        var draft = new ModuleDraft(typeof(TModule));
        configure?.Invoke(new(draft, EnsureMutable));
        _modules.Add(typeof(TModule), draft);
        return this;
    }

    public BrainCompositionBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        if (!_modules.TryGetValue(typeof(TModule), out var draft))
            { throw new InvalidOperationException($"{typeof(TModule).Name} is not declared in this application."); }
        var copy = draft.Copy();
        configure(new(copy, EnsureMutable));
        _modules[typeof(TModule)] = copy;
        return this;
    }

    public BrainComposition Build()
    {
        if (_built is not null) { return _built; }
        var modules = ModuleComposition.Resolve(_modules.Values.Select(m => m.Compile()).ToArray());
        ApplicationConfigurationTransport.ValidatePublicSettings(modules);
        return _built = new(modules);
    }

    private void EnsureMutable()
    {
        if (_built is not null) { throw new InvalidOperationException("The composition is frozen; create a new builder to change it."); }
    }
}
