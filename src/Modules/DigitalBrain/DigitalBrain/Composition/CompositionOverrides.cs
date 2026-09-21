namespace DigitalBrain.Core;

/// <summary>Explicit member overrides for modules already declared by an application.</summary>
public sealed class CompositionOverrides
{
    private readonly Dictionary<Type, ModuleDraft> _modules = [];
    private string? _serialized;

    public CompositionOverrides ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        var draft = _modules.TryGetValue(typeof(TModule), out var previous) ? previous.Copy() : new(typeof(TModule));
        configure(new(draft, EnsureMutable));
        _modules[typeof(TModule)] = draft;
        return this;
    }

    public string Serialize()
    {
        if (_serialized is not null) { return _serialized; }
        return _serialized = CompositionOverrideTransport.Write(_modules.Values.Select(draft =>
        {
            if (draft.LocalServices.Count > 0)
            { throw new NotSupportedException("Local service substitutions cannot cross process boundaries. Use a hosted provider or endpoint fixture."); }
            var contract = draft.Contract ?? throw new InvalidOperationException($"{draft.Type.Name} has no configurable options.");
            return new CompositionOverrideTransport.Entry(draft.Type.FullName!, draft.Replace
                ? contract.WriteReplacement(draft.Options!) : contract.WriteOverride(draft.Options!, draft.Assigned));
        }).ToArray());
    }

    private void EnsureMutable()
    {
        if (_serialized is not null) { throw new InvalidOperationException("These overrides are frozen; create a new builder to change them."); }
    }
}