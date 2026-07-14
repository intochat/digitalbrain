using System.Collections.ObjectModel;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;

namespace DigitalBrain.Features.Testing;

public enum FeatureExecutionStatus
{
    Succeeded,
    Denied,
    Failed
}

public sealed class FeatureScenarioResult
{
    internal FeatureScenarioResult(FeatureExecutionStatus status, string? message, bool duplicate)
    {
        Status = status;
        Message = message;
        Duplicate = duplicate;
    }

    public FeatureExecutionStatus Status { get; }
    public string? Message { get; }
    public bool Duplicate { get; }
}

public sealed class FeatureScenarioContext : IFeatureContext
{
    private static readonly DateTimeOffset DefaultTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly object _gate = new();
    private readonly AsyncLocal<ExecutionBudget?> _currentBudget = new();
    private Task _executionTail = Task.CompletedTask;
    private readonly Dictionary<string, GmailMessage> _messages = new(StringComparer.Ordinal);
    private readonly Dictionary<ModelRequestKey, ModelResponse> _modelResponses = [];
    private readonly Dictionary<string, Task<FeatureScenarioResult>> _activeInputs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedInputs = new(StringComparer.Ordinal);
    private readonly List<ModelRequest> _modelRequests = [];
    private readonly List<MemoryFact> _memoryFacts = [];
    private readonly IntentSet _committed = new(int.MaxValue);
    private readonly ReadOnlyCollection<ModelRequest> _modelRequestView;
    private readonly ScenarioClock _clock = new(DefaultTime);
    private readonly ScenarioIdentifiers _identifiers = new();
    private readonly ScenarioState _state;
    private readonly ScenarioMemoryRecall _memoryRecall;
    private readonly ScenarioMemoryRemember _memoryRemember;
    private readonly ScenarioIntentBuffer _intents;
    private bool _gmailGranted;
    private int _gmailReadCount;
    private int _modelCallCount;

    public FeatureScenarioContext()
    {
        _modelRequestView = _modelRequests.AsReadOnly();
        _state = new ScenarioState(_committed);
        _memoryRecall = new ScenarioMemoryRecall(this);
        _memoryRemember = new ScenarioMemoryRemember(_committed);
        _intents = new ScenarioIntentBuffer(_committed);
        GmailReader = new ScenarioGmailReader(this);
        Models = new ScenarioModelWorkflow(this);
    }

    public IGmailMessageReader GmailReader { get; }
    public IFeatureClock Clock => _clock;
    public IFeatureIdentifiers Identifiers => _identifiers;
    public IFeatureState State => _state;
    public IMemoryRecall MemoryRecall => _memoryRecall;
    public IMemoryRemember MemoryRemember => _memoryRemember;
    public IModelWorkflow Models { get; }
    public IFeatureIntentBuffer Intents => _intents;
    public IReadOnlyList<TextSurfaceIntent> Surfaces => _committed.SurfaceView;
    public IReadOnlyList<EventIntent> Events => _committed.EventView;
    public IReadOnlyList<ExternalEffectIntent> ExternalEffects => _committed.ExternalEffectView;
    public IReadOnlyList<MemoryRememberIntent> MemoryWrites => _committed.MemoryWriteView;
    public IReadOnlyList<ModelRequest> ModelRequests => _modelRequestView;
    public FeatureScenarioResult? LastResult { get; private set; }
    public int GmailReadCount => Volatile.Read(ref _gmailReadCount);
    public int ModelCallCount => Volatile.Read(ref _modelCallCount);

    public void Reset()
    {
        lock (_gate)
        {
            if (_activeInputs.Count != 0)
            {
                throw new InvalidOperationException("Cannot reset while Feature executions are active.");
            }

            _messages.Clear();
            _modelResponses.Clear();
            _completedInputs.Clear();
            _modelRequests.Clear();
            _memoryFacts.Clear();
            _committed.Clear();
            _gmailGranted = false;
            LastResult = null;
            _gmailReadCount = 0;
            _modelCallCount = 0;
            _clock.Set(DefaultTime);
            _identifiers.Reset();
            _executionTail = Task.CompletedTask;
        }
    }

    public void ConfigureMessage(GmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            _messages.Add(message.MessageId, message);
        }
    }

    public void SetGmailReadGrant(bool granted)
    {
        lock (_gate)
        {
            _gmailGranted = granted;
        }
    }

    public void ConfigureModelResponse(ModelRequest request, string response)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            _modelResponses.Add(ModelRequestKey.From(request), new ModelResponse(response));
        }
    }

    public void ConfigureMemoryFact(MemoryFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        lock (_gate)
        {
            _memoryFacts.Add(fact);
        }
    }

    public void SetTime(DateTimeOffset utcNow) => _clock.Set(utcNow);

    public async Task<FeatureScenarioResult> ExecuteAsync(IFeature feature, FeatureInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<FeatureScenarioResult>? completion = null;
        Task? predecessor = null;
        TaskCompletionSource? turn = null;
        Task<FeatureScenarioResult>? active;
        lock (_gate)
        {
            if (_completedInputs.Contains(input.InputId))
            {
                return SetLast(new FeatureScenarioResult(FeatureExecutionStatus.Succeeded, null, duplicate: true));
            }

            if (!_activeInputs.TryGetValue(input.InputId, out active))
            {
                completion = new TaskCompletionSource<FeatureScenarioResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                active = completion.Task;
                _activeInputs.Add(input.InputId, active);
                predecessor = _executionTail;
                turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _executionTail = turn.Task;
            }
        }

        if (completion is null)
        {
            FeatureScenarioResult observed;
            try
            {
                observed = await active!.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return await ExecuteAsync(feature, input, cancellationToken);
            }

            if (observed.Status == FeatureExecutionStatus.Succeeded)
            {
                return SetLast(new FeatureScenarioResult(FeatureExecutionStatus.Succeeded, null, duplicate: true));
            }

            return await ExecuteAsync(feature, input, cancellationToken);
        }

        FeatureScenarioResult? executionResult = null;
        var cancelled = false;
        try
        {
            await predecessor!.WaitAsync(cancellationToken);
            var staged = new IntentSet(32);
            var executionContext = new ScenarioExecutionContext(this, staged);
            _currentBudget.Value = new ExecutionBudget();
            await feature.HandleAsync(input, executionContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _committed.Merge(staged);
                _completedInputs.Add(input.InputId);
            }

            executionResult = new FeatureScenarioResult(FeatureExecutionStatus.Succeeded, null, duplicate: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            throw;
        }
        catch (FeatureCapabilityDeniedException exception)
        {
            executionResult = new FeatureScenarioResult(FeatureExecutionStatus.Denied, exception.CapabilityId, duplicate: false);
        }
        catch (Exception exception)
        {
            executionResult = new FeatureScenarioResult(FeatureExecutionStatus.Failed, exception.Message, duplicate: false);
        }
        finally
        {
            _currentBudget.Value = null;
            if (cancelled && !predecessor!.IsCompleted)
            {
                _ = CompleteTurnAfterAsync(predecessor, turn!);
            }
            else
            {
                turn!.TrySetResult();
            }

            lock (_gate)
            {
                _activeInputs.Remove(input.InputId);
            }

            if (cancelled)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                completion.TrySetResult(executionResult!);
            }
        }

        return SetLast(executionResult!);
    }

    private static async Task CompleteTurnAfterAsync(Task predecessor, TaskCompletionSource turn)
    {
        await predecessor.ConfigureAwait(false);
        turn.TrySetResult();
    }

    private FeatureScenarioResult SetLast(FeatureScenarioResult result)
    {
        LastResult = result;
        return result;
    }

    private sealed class ScenarioExecutionContext : IFeatureContext
    {
        public ScenarioExecutionContext(FeatureScenarioContext owner, IntentSet staged)
        {
            Clock = owner.Clock;
            Identifiers = owner.Identifiers;
            State = new ScenarioState(staged, owner.State.Read());
            MemoryRecall = owner.MemoryRecall;
            MemoryRemember = new ScenarioMemoryRemember(staged);
            Models = owner.Models;
            Intents = new ScenarioIntentBuffer(staged);
        }

        public IFeatureClock Clock { get; }
        public IFeatureIdentifiers Identifiers { get; }
        public IFeatureState State { get; }
        public IMemoryRecall MemoryRecall { get; }
        public IMemoryRemember MemoryRemember { get; }
        public IModelWorkflow Models { get; }
        public IFeatureIntentBuffer Intents { get; }
    }

    private sealed class ScenarioGmailReader(FeatureScenarioContext owner) : IGmailMessageReader
    {
        public Task<GmailMessage> ReadAsync(GmailMessageReadRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner._currentBudget.Value?.CapabilityRead();
            lock (owner._gate)
            {
                if (!owner._gmailGranted)
                {
                    throw new FeatureCapabilityDeniedException(GoogleCapabilityIds.GmailMessageRead);
                }

                if (!owner._messages.TryGetValue(request.MessageId, out var message))
                {
                    throw new InvalidOperationException($"No Gmail message configured for {request.MessageId}.");
                }

                Interlocked.Increment(ref owner._gmailReadCount);
                return Task.FromResult(message);
            }
        }
    }

    private sealed class ScenarioModelWorkflow(FeatureScenarioContext owner) : IModelWorkflow
    {
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(request);
            owner._currentBudget.Value?.ModelCall();
            lock (owner._gate)
            {
                Interlocked.Increment(ref owner._modelCallCount);
                owner._modelRequests.Add(request);
                if (!owner._modelResponses.TryGetValue(ModelRequestKey.From(request), out var response))
                {
                    throw new InvalidOperationException($"No model response configured for {request.WorkflowId}.");
                }

                return Task.FromResult(response);
            }
        }
    }

    private sealed class ScenarioMemoryRecall(FeatureScenarioContext owner) : IMemoryRecall
    {
        public Task<IReadOnlyList<MemoryFact>> RecallAsync(MemoryRecallRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(request);
            owner._currentBudget.Value?.CapabilityRead();
            lock (owner._gate)
            {
                var count = Math.Min(request.Limit, owner._memoryFacts.Count);
                var matches = new MemoryFact[count];
                owner._memoryFacts.CopyTo(0, matches, 0, count);
                return Task.FromResult<IReadOnlyList<MemoryFact>>(matches);
            }
        }
    }

    private sealed class ScenarioClock(DateTimeOffset utcNow) : IFeatureClock
    {
        private DateTimeOffset _utcNow = utcNow;
        public DateTimeOffset UtcNow => _utcNow;
        public void Set(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class ScenarioIdentifiers : IFeatureIdentifiers
    {
        private int _value;

        public string Next(string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            if (scope.Length > 128)
            {
                throw new ArgumentException("Scope cannot exceed 128 characters.", nameof(scope));
            }

            return $"{scope}-{Interlocked.Increment(ref _value):D8}";
        }

        public void Reset() => _value = 0;
    }

    private sealed class ScenarioState : IFeatureState
    {
        private readonly IntentSet _intents;
        private readonly FeatureState _initial;

        public ScenarioState(IntentSet intents, FeatureState? initial = null)
        {
            _intents = intents;
            _initial = initial ?? new FeatureState("{}");
        }

        public FeatureState Read() => _intents.State ?? _initial;

        public void Replace(FeatureState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            _intents.SetState(state);
        }
    }

    private sealed class ScenarioMemoryRemember(IntentSet intents) : IMemoryRemember
    {
        public void Remember(MemoryRememberIntent intent) => intents.AddMemoryWrite(intent);
    }

    private sealed class ScenarioIntentBuffer(IntentSet intents) : IFeatureIntentBuffer
    {
        public void AddTextSurface(TextSurfaceIntent intent) => intents.AddSurface(intent);
        public void EmitEvent(EventIntent intent) => intents.AddEvent(intent);
        public void ProposeExternalEffect(ExternalEffectIntent intent) => intents.AddExternalEffect(intent);
    }

    private sealed class IntentSet
    {
        private readonly int _maximumIntentCount;
        private readonly List<TextSurfaceIntent> _surfaces = [];
        private readonly List<EventIntent> _events = [];
        private readonly List<ExternalEffectIntent> _externalEffects = [];
        private readonly List<MemoryRememberIntent> _memoryWrites = [];
        private readonly ReadOnlyCollection<TextSurfaceIntent> _surfaceView;
        private readonly ReadOnlyCollection<EventIntent> _eventView;
        private readonly ReadOnlyCollection<ExternalEffectIntent> _externalEffectView;
        private readonly ReadOnlyCollection<MemoryRememberIntent> _memoryWriteView;
        private int _intentCount;

        public IntentSet(int maximumIntentCount)
        {
            _maximumIntentCount = maximumIntentCount;
            _surfaceView = _surfaces.AsReadOnly();
            _eventView = _events.AsReadOnly();
            _externalEffectView = _externalEffects.AsReadOnly();
            _memoryWriteView = _memoryWrites.AsReadOnly();
        }

        public IReadOnlyList<TextSurfaceIntent> SurfaceView => _surfaceView;
        public IReadOnlyList<EventIntent> EventView => _eventView;
        public IReadOnlyList<ExternalEffectIntent> ExternalEffectView => _externalEffectView;
        public IReadOnlyList<MemoryRememberIntent> MemoryWriteView => _memoryWriteView;
        public FeatureState? State { get; private set; }

        public void AddSurface(TextSurfaceIntent intent)
        {
            Add(intent);
            _surfaces.Add(intent);
        }

        public void AddEvent(EventIntent intent)
        {
            Add(intent);
            _events.Add(intent);
        }

        public void AddExternalEffect(ExternalEffectIntent intent)
        {
            Add(intent);
            _externalEffects.Add(intent);
        }

        public void AddMemoryWrite(MemoryRememberIntent intent)
        {
            Add(intent);
            _memoryWrites.Add(intent);
        }

        public void SetState(FeatureState state)
        {
            if (State is null)
            {
                Reserve();
            }

            State = state;
        }

        public void Merge(IntentSet source)
        {
            foreach (var surface in source._surfaces)
            {
                AddSurface(surface);
            }

            foreach (var item in source._events)
            {
                AddEvent(item);
            }

            foreach (var item in source._externalEffects)
            {
                AddExternalEffect(item);
            }

            foreach (var item in source._memoryWrites)
            {
                AddMemoryWrite(item);
            }

            if (source.State is not null)
            {
                SetState(source.State);
            }
        }

        public void Clear()
        {
            _surfaces.Clear();
            _events.Clear();
            _externalEffects.Clear();
            _memoryWrites.Clear();
            State = null;
            _intentCount = 0;
        }

        private void Add<T>(T intent) where T : class
        {
            ArgumentNullException.ThrowIfNull(intent);
            Reserve();
        }

        private void Reserve()
        {
            if (_intentCount == _maximumIntentCount)
            {
                throw new InvalidOperationException("A Feature run cannot buffer more than 32 intents.");
            }

            _intentCount++;
        }
    }

    private readonly record struct ModelRequestKey(string WorkflowId, string Prompt, string LogicalOperationKey)
    {
        public static ModelRequestKey From(ModelRequest request) =>
            new(request.WorkflowId, request.Prompt, request.LogicalOperationKey);
    }

    private sealed class ExecutionBudget
    {
        private int _capabilityReads;
        private int _modelCalls;

        public void CapabilityRead()
        {
            if (Interlocked.Increment(ref _capabilityReads) > 20)
            {
                throw new InvalidOperationException("A Feature run cannot perform more than 20 capability reads.");
            }
        }

        public void ModelCall()
        {
            if (Interlocked.Increment(ref _modelCalls) > 4)
            {
                throw new InvalidOperationException("A Feature run cannot perform more than 4 model calls.");
            }
        }
    }
}
