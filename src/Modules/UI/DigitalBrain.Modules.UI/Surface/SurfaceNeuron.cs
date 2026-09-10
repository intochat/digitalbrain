using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SurfaceState> state)
    : Neuron<SurfaceState>(runtime, state), ISurface
{
    public Task<Accepted<SurfaceOpenReceipt>> Open(OpenSurface command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("open"), command, UIJson.Default.OpenSurface, UIJson.Default.AcceptedSurfaceOpenReceipt, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.SurfaceKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Title);
            var scene = new SurfaceScene(arguments.SurfaceKey, arguments.Title, arguments.Root);
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(scene, UIJson.Default.SurfaceScene)));
            var previousKeys = Components(State?.Scenes.FirstOrDefault(existing => existing.SurfaceKey == scene.SurfaceKey)?.Root)
                .Select(component => component.Key).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var additions = Components(scene.Root)
                .Where(component => component.Key is { Length: > 0 } key && !previousKeys.Contains(key))
                .DistinctBy(component => component.Key, StringComparer.Ordinal).ToList();
            var receipt = new SurfaceOpenReceipt(arguments.Id, fingerprint, additions);
            // Carry the computed receipt so queued opens publish exactly the additions their callers saw.
            var body = new JsonObject
            {
                ["command"] = JsonSerializer.SerializeToNode(arguments, UIJson.Default.OpenSurface),
                ["receipt"] = JsonSerializer.SerializeToNode(receipt, UIJson.Default.SurfaceOpenReceipt),
            };
            var work = Schedule(Signal.Create(UIVocabulary.Opening, body.ToJsonString()));
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

            var work = Schedule(UIBodies.Signal(UIVocabulary.Activating, arguments, UIJson.Default.ActivateControl));
            return new Accepted<ControlActivation>(new(arguments.SurfaceKey, arguments.ControlId, arguments.Intent), work);
        });

    [ReadOnly]
    public Task<SurfaceState> Read() => Task.FromResult(State ?? new SurfaceState([]));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case UIVocabulary.Opening:
                await OpenAsync(delivery, cancellationToken).ConfigureAwait(true);
                break;
            case UIVocabulary.Activating:
                var command = UIBodies.Read(delivery, UIJson.Default.ActivateControl);
                await FireAsync(UIBodies.Signal(UIVocabulary.ControlActivated,
                    new ControlActivation(command.SurfaceKey, command.ControlId, command.Intent), UIJson.Default.ControlActivation),
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case UIVocabulary.ActivityChanged:
                var activity = UIBodies.Read(delivery, UIJson.Default.ActivityChanged).Activity;
                var previous = State?.Activities?.FirstOrDefault(item => item.Id == activity.Id);
                if (previous is not null && previous.Version >= activity.Version)
                {
                    return;
                }

                var current = State ?? new SurfaceState([]);
                await SaveAsync(current with
                {
                    Activities = [.. (current.Activities ?? []).Where(item => item.Id != activity.Id)
                        .Append(activity).OrderByDescending(item => item.UpdatedAt).Take(100)],
                }, cancellationToken).ConfigureAwait(true);
                break;
        }
    }

    private async Task OpenAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(delivery.Signal.Body)!;
        var command = body["command"].Deserialize(UIJson.Default.OpenSurface)!;
        var receipt = body["receipt"].Deserialize(UIJson.Default.SurfaceOpenReceipt)!;
        var current = State ?? new SurfaceState([]);
        var scene = new SurfaceScene(command.SurfaceKey, command.Title, command.Root);
        await SaveAsync(current with
        {
            Scenes = BoundedList.Append(current.Scenes.Where(item => item.SurfaceKey != command.SurfaceKey), scene, 64),
            OpenReceipts = BoundedList.Append((current.OpenReceipts ?? []).Where(item => item.CommandId != command.Id), receipt, 64),
        }, cancellationToken).ConfigureAwait(true);
        await FireAsync(UIBodies.Signal(UIVocabulary.SurfaceOpened,
            new SurfaceOpened(command.Id, Id, command.SurfaceKey, command.Title, receipt), UIJson.Default.SurfaceOpened),
            cancellationToken: cancellationToken).ConfigureAwait(true);
        foreach (var component in receipt.AddedComponents)
        {
            var eventId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"component-added:{Id}:{command.Id}:{command.SurfaceKey}:{component.Key}")).AsSpan(0, 16)).ToString();
            await FireAsync(UIBodies.Signal(UIVocabulary.ComponentAdded,
                new ComponentAdded(command.Id, Id, command.SurfaceKey, component, eventId), UIJson.Default.ComponentAdded),
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }
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
