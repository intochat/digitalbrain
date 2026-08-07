using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.AttemptAccepted", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.AttemptCancelled", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.AttemptFailed", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.AttemptOutcomeUncertain", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.AttemptProgressed", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.AttemptSucceeded", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.AttemptWaiting", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.CompleteUserAction", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.DenyUserAction", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.PrepareTaskOperation", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.ReadTaskOperation", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.StartTask", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.TaskSnapshot", false),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.TransitionTaskOperation", true),
        ("DigitalBrain.Tasks.TaskNeuron", "DigitalBrain.Tasks.UserActionRequired", true),
        ("DigitalBrain.Tasks.WorkerDispatchRelayNeuron", "DigitalBrain.Tasks.RelayWorkerAccept", true),
        ("DigitalBrain.Tasks.WorkerDispatchRelayNeuron", "DigitalBrain.Tasks.RelayWorkerCancel", true),
        ("DigitalBrain.Tasks.WorkerDispatchRelayNeuron", "DigitalBrain.Tasks.RelayWorkerContinue", true),
    ];
}
