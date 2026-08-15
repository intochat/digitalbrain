using DigitalBrain.Abstractions;
using Orleans.Runtime;

namespace DigitalBrain.Core;

public static class GrainCallerContext
{
    private const string SourceKey = "db.grain-caller-source";
    private const string AuthorizationInitiatorKey = "db.mcp-authorization-initiator";

    internal static CallerScope Enter(GrainId? source)
    {
        var previousSource = RequestContext.Get(SourceKey);
        var previousInitiator = RequestContext.Get(AuthorizationInitiatorKey);

        SetSource(source);

        if (previousInitiator is null
            && source is { } attributed
            && TryParseNeuronId(attributed, out _))
        {
            RequestContext.Set(AuthorizationInitiatorKey, attributed);
        }

        return new CallerScope(previousSource, previousInitiator);
    }

    public static bool TryGetNeuronId(out NeuronId caller)
    {
        if (RequestContext.Get(SourceKey) is not GrainId source
            || !TryParseNeuronId(source, out caller))
        {
            caller = default;
            return false;
        }

        return true;
    }

    public static bool TryGetAuthorizationInitiator(out NeuronId initiator)
    {
        if (RequestContext.Get(AuthorizationInitiatorKey) is GrainId source
            && TryParseNeuronId(source, out initiator)
            && initiator != default)
        {
            return true;
        }

        initiator = default;
        return false;
    }

    private static void SetSource(GrainId? source)
    {
        if (source is { } identified)
        {
            RequestContext.Set(SourceKey, identified);
            return;
        }

        RequestContext.Remove(SourceKey);
    }

    private static bool TryParseNeuronId(GrainId source, out NeuronId caller)
    {
        if (source.Key.ToString() is not { Length: > 0 } key
            || source.Type.ToString() is not { Length: > 0 } type)
        {
            caller = default;
            return false;
        }

        var separator = key.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || separator == key.Length - 1)
        {
            caller = default;
            return false;
        }

        caller = NeuronId.FromGrainKey(type, key);
        return true;
    }

    private static void Restore(string key, object? previous)
    {
        if (previous is null)
        {
            RequestContext.Remove(key);
            return;
        }

        RequestContext.Set(key, previous);
    }

    internal readonly struct CallerScope : IDisposable
    {
        private readonly object? _previousSource;
        private readonly object? _previousInitiator;

        public CallerScope(object? previousSource, object? previousInitiator)
        {
            _previousSource = previousSource;
            _previousInitiator = previousInitiator;
        }

        public void Dispose()
        {
            Restore(SourceKey, _previousSource);
            Restore(AuthorizationInitiatorKey, _previousInitiator);
        }
    }
}
