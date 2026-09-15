using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Telegram;

/// <summary>Installs the default graph once; command replay never restarts a user's stopped behavior.</summary>
public sealed class TelegramBehaviorSetup(IGrainFactory grains, TelegramReminderBehavior definitions) : ITelegramBehaviorSetup
{
    public async Task EnsureAsync(long userId, CancellationToken cancellationToken)
    {
        var identity = TelegramReminderBehavior.Identity(userId);
        var behavior = grains.GetGrain<IBehavior>(identity.ToGrainId());
        var current = await behavior.Read().ConfigureAwait(false);
        if (current.Definition is null)
        {
            await behavior.Save(new(definitions.Build(userId), Command(identity, "save"))).ConfigureAwait(false);
            await Drain(behavior, cancellationToken).ConfigureAwait(false);
        }
        // This exact identity is replayed for every ingress. After the first activation,
        // Start returns the original receipt even if the owner later stopped the graph.
        await behavior.Start(new(Command(identity, "start"))).ConfigureAwait(false);
        await Drain(behavior, cancellationToken).ConfigureAwait(false);
        current = await behavior.Read().ConfigureAwait(false);
        if (current.Error is not null || current.Status is not (BehaviorStatus.Running or BehaviorStatus.Stopped))
        {
            throw new InvalidOperationException(current.Error ?? "Telegram behavior is not ready.");
        }
    }

    private static CommandId Command(NeuronId identity, string operation)
        => new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{identity}/v1/{operation}")).AsSpan(0, 16)));

    private static async Task Drain(IBehavior behavior, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (await behavior.ReadPendingCount().ConfigureAwait(false) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Telegram behavior setup is still pending; retry this receipt.");
            }
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }
}
