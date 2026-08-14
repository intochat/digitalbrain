using System.Text.Json;
using Brain.Modules.Behavior.Contracts;
using Orleans.Runtime;

namespace Brain.Modules.Behavior;

public sealed class BehaviorGrain(
    [PersistentState("behavior", "Default")]
    IPersistentState<BehaviorState> state) : Grain, IBehaviorGrain
{
    private readonly IPersistentState<BehaviorState> _state = state;

    public async Task<BehaviorSnapshot> PublishAsync(PublishBehaviorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Source)
            || string.IsNullOrWhiteSpace(request.Principal)
            || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("A behavior revision requires name, source, principal, and idempotency key.");
        }

        ValidateSource(request.Source);
        EnsureIdentity();
        if (_state.State.ProcessedRequests.Add(request.IdempotencyKey))
        {
            _state.State.Revisions.Add(new BehaviorRevision(
                _state.State.Revisions.Count + 1,
                request.Name.Trim(),
                request.Source,
                request.Principal));
            await _state.WriteStateAsync();
        }

        return Snapshot();
    }

    public async Task<BehaviorSnapshot> ActivateAsync(int revision, string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        EnsureIdentity();
        if (_state.State.Revisions.All(candidate => candidate.Revision != revision))
        {
            throw new KeyNotFoundException($"Behavior revision '{revision}' does not exist.");
        }

        if (_state.State.ProcessedRequests.Add(idempotencyKey))
        {
            _state.State.ActiveRevision = revision;
            await _state.WriteStateAsync();
        }

        return Snapshot();
    }

    public async Task<BehaviorSnapshot> RunAsync(RunBehaviorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RunId)
            || string.IsNullOrWhiteSpace(request.Input)
            || string.IsNullOrWhiteSpace(request.Principal)
            || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("A behavior run requires run id, input, principal, and idempotency key.");
        }

        EnsureIdentity();
        var active = _state.State.ActiveRevision is { } activeRevision
            ? _state.State.Revisions.Single(revision => revision.Revision == activeRevision)
            : throw new InvalidOperationException("Behavior has no active revision.");
        if (_state.State.ProcessedRequests.Add(request.IdempotencyKey))
        {
            _state.State.Runs.Insert(0, new BehaviorRun(
                request.RunId,
                active.Revision,
                request.Input,
                Render(active.Source, request.Input),
                request.Principal));
            await _state.WriteStateAsync();
        }

        return Snapshot();
    }

    public Task<BehaviorSnapshot> ReadAsync()
    {
        EnsureIdentity();
        return Task.FromResult(Snapshot());
    }

    private void EnsureIdentity()
    {
        if (_state.State.BehaviorId.Length != 0)
        {
            return;
        }

        var key = this.GetPrimaryKeyString();
        _state.State.BehaviorId = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }

    private BehaviorSnapshot Snapshot()
        => new(
            _state.State.BehaviorId,
            _state.State.Revisions.Count == 0 ? "missing" : "ready",
            _state.State.Revisions.Count,
            _state.State.ActiveRevision,
            [.. _state.State.Revisions.OrderByDescending(static revision => revision.Revision)],
            [.. _state.State.Runs]);

    private static void ValidateSource(string source)
    {
        using var document = JsonDocument.Parse(source);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("template", out var template)
            || template.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(template.GetString()))
        {
            throw new JsonException("Behavior source requires a non-empty string 'template'.");
        }
    }

    private static string Render(string source, string input)
    {
        using var document = JsonDocument.Parse(source);
        return document.RootElement.GetProperty("template").GetString()!
            .Replace("{{input}}", input, StringComparison.Ordinal);
    }
}
