using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.SurfaceType)]
internal sealed class SurfaceNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<SurfaceState>> state)
    : Neuron<SurfaceState>(runtime, state), ISurface
{
    public Task<Accepted<SurfaceOpenReceipt>> Open(OpenSurface command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("open"), command, UIJson.Default.OpenSurface, UIJson.Default.AcceptedSurfaceOpenReceipt, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.SurfaceKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Title);
            var scene = new SurfaceScene(arguments.SurfaceKey, arguments.Title, arguments.Root);
            // This receipt is advisory: it is what the caller can show immediately.
            var receipt = CreateReceipt(arguments, scene, State);
            var work = Schedule(Signal.FromJson(UIVocabulary.SurfaceOpening, arguments, UIJson.Default.OpenSurface));
            return new Accepted<SurfaceOpenReceipt>(receipt, work);
        });

    public Task<Accepted<ControlActivation>> Activate(ActivateControl command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("activate"), command, UIJson.Default.ActivateControl, UIJson.Default.AcceptedControlActivation, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.SurfaceKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.ControlId);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Intent);
            var scene = State?.Scenes.FirstOrDefault(item => item.SurfaceKey == arguments.SurfaceKey);
            // This validation replaces the deleted HTTP 404 pre-check.
            if (!Components(scene?.Root).Any(component => component.Kind == "button" && component.Key == arguments.ControlId))
            {
                throw new ArgumentException($"Button '{arguments.ControlId}' is not on surface '{arguments.SurfaceKey}'. Open a surface containing that button, then activate its control id.", nameof(command));
            }

            var work = Schedule(Signal.FromJson(UIVocabulary.SurfaceActivating, arguments, UIJson.Default.ActivateControl));
            return new Accepted<ControlActivation>(new(arguments.SurfaceKey, arguments.ControlId, arguments.Intent), work);
        });

    [ReadOnly]
    public Task<SurfaceState> Read() => Task.FromResult(State ?? new SurfaceState([]));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        SurfaceState? next = null;
        switch (delivery.Signal.Type)
        {
            case UIVocabulary.SurfaceOpening:
                next = OpenScene(delivery);
                break;
            case UIVocabulary.SurfaceActivating:
                if (Body(delivery, UIJson.Default.ActivateControl) is not { } command)
                {
                    break;
                }

                next = State ?? new SurfaceState([]);
                var scene = next.Scenes.FirstOrDefault(item => item.SurfaceKey == command.SurfaceKey);
                if (!Components(scene?.Root).Any(component => component.Kind == "button" && component.Key == command.ControlId))
                {
                    Announce(Signal.FromJson(UIVocabulary.ControlRefused,
                        new ControlRefused(command.SurfaceKey, command.ControlId, command.Intent, "The button is no longer on the surface."),
                        UIJson.Default.ControlRefused));
                    break;
                }

                Announce(Signal.FromJson(UIVocabulary.ControlActivated,
                    new ControlActivation(command.SurfaceKey, command.ControlId, command.Intent), UIJson.Default.ControlActivation));
                break;
            case UIVocabulary.ActivityChanged:
                if (Body(delivery, UIJson.Default.ActivityChanged) is not { } activityBody)
                {
                    break;
                }

                var activity = activityBody.Activity;
                var previous = State?.Activities?.FirstOrDefault(item => item.Id == activity.Id);
                if (previous is not null && previous.Version >= activity.Version)
                {
                    break;
                }

                var current = State ?? new SurfaceState([]);
                next = current with
                {
                    Activities = [.. (current.Activities ?? []).Where(item => item.Id != activity.Id)
                        .Append(activity).OrderByDescending(item => item.UpdatedAt).Take(100)],
                };
                break;
        }

        if (next is not null)
        {
            await SaveAsync(next, cancellationToken).ConfigureAwait(true);
        }
    }

    private SurfaceState? OpenScene(SignalDelivery delivery)
    {
        if (Body(delivery, UIJson.Default.OpenSurface) is not { } command)
        {
            return null;
        }

        var current = State ?? new SurfaceState([]);
        var scene = new SurfaceScene(command.SurfaceKey, command.Title, command.Root);
        var receipt = CreateReceipt(command, scene, current);
        Announce(Signal.FromJson(UIVocabulary.SurfaceOpened,
            new SurfaceOpened(command.Id, Id, command.SurfaceKey, command.Title, receipt), UIJson.Default.SurfaceOpened));
        foreach (var component in receipt.AddedComponents)
        {
            // Identical queued scenes mint the same ids so clients deduplicating by event id cannot double-add.
            var eventId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"component-added:{Id}:{receipt.Fingerprint}:{command.SurfaceKey}:{component.Key}")).AsSpan(0, 16)).ToString();
            Announce(Signal.FromJson(UIVocabulary.ComponentAdded,
                new ComponentAdded(command.Id, Id, command.SurfaceKey, component, eventId), UIJson.Default.ComponentAdded));
        }

        return current with
        {
            Scenes = BoundedList.Append(current.Scenes.Where(item => item.SurfaceKey != command.SurfaceKey), scene, 64),
            OpenReceipts = BoundedList.Append((current.OpenReceipts ?? []).Where(item => item.CommandId != command.Id), receipt, 64),
        };
    }

    private static SurfaceOpenReceipt CreateReceipt(OpenSurface command, SurfaceScene scene, SurfaceState? current)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(scene, UIJson.Default.SurfaceScene)));
        var previousKeys = Components(current?.Scenes.FirstOrDefault(existing => existing.SurfaceKey == scene.SurfaceKey)?.Root)
            .Select(component => component.Key).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var additions = Components(scene.Root)
            .Where(component => component.Key is { Length: > 0 } key && !previousKeys.Contains(key))
            .DistinctBy(component => component.Key, StringComparer.Ordinal).ToList();
        return new SurfaceOpenReceipt(command.Id, fingerprint, additions);
    }

    private static IEnumerable<SurfaceComponent> Components(SurfaceComponent? root)
    {
        if (root is null)
        {
            yield break;
        }

        yield return root;
        foreach (var child in root.Children ?? [])
        {
            foreach (var descendant in Components(child))
            {
                yield return descendant;
            }
        }
    }
}
