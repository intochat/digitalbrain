namespace DigitalBrain.Core;

public sealed class CompositionOverrides
{
    private readonly Dictionary<Type, List<Delegate>> _optionEdits = [];
    private readonly HashSet<Type> _localServiceModules = [];
    private readonly HashSet<Type> _omittedModules = [];
    private string? _serialized;

    public CompositionOverrides ConfigureModule<TModule, TOptions>(Action<TOptions> configureOptions)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configureOptions);
        if (!_optionEdits.TryGetValue(typeof(TModule), out var edits)) { _optionEdits[typeof(TModule)] = edits = []; }
        edits.Add(configureOptions);
        return this;
    }

    public CompositionOverrides ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        var draft = new ModuleDraft(typeof(TModule));
        configure(new(draft, EnsureMutable));
        if (draft.LocalServices.Count > 0) { _localServiceModules.Add(typeof(TModule)); }
        return this;
    }

    public CompositionOverrides WithoutModule<TModule>() where TModule : class, IModule, new()
    {
        EnsureMutable();
        _omittedModules.Add(typeof(TModule));
        return this;
    }

    public string Serialize()
    {
        if (_serialized is not null) { return _serialized; }
        if (_localServiceModules.Count > 0)
        { throw new NotSupportedException("Local service substitutions cannot cross process boundaries. Use a hosted provider or endpoint fixture."); }
        return _serialized = CompositionOverrideTransport.Publish(
            [.. _optionEdits.Keys.Concat(_omittedModules).Distinct().Select(type =>
                new CompositionOverrideTransport.ModuleEdit(type, _optionEdits.GetValueOrDefault(type) ?? [], _omittedModules.Contains(type)))]);
    }

    private void EnsureMutable()
    {
        if (_serialized is not null) { throw new InvalidOperationException("These overrides are frozen; create a new builder to change them."); }
    }
}
