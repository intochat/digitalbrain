using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using Orleans.Journaling;

namespace DigitalBrain.OS;

internal sealed class PreRailBehaviorContext(
    IGrainFactory grains,
    BehaviorExecutionMetadata execution,
    TimeProvider time,
    IDurableDictionary<string, byte[]> state) : IBehaviorContext
{
    public BehaviorExecutionMetadata Execution { get; } = execution;

    public DateTimeOffset UtcNow => time.GetUtcNow();

    public CommandId DeterministicCommandId(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var seed = SHA256.HashData(Encoding.UTF8.GetBytes($"{Execution.Behavior}/{Execution.Execution.Value}/{purpose}"));

        return new CommandId(new Guid(seed.AsSpan(0, 16)));
    }

    public TContract Get<TContract>(string name)
        where TContract : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return grains.GetGrain<TContract>(NeuronId.For<TContract>(Execution.Owner, name).ToGrainId());
    }

    public ValueTask<T?> ReadStateAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        return new(state.TryGetValue(key, out var stored)
            ? JsonSerializer.Deserialize<T>(stored)
            : default);
    }

    public void SetState<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        state[key] = JsonSerializer.SerializeToUtf8Bytes(value);
    }
}
