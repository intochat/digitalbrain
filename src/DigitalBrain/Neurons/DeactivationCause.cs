namespace DigitalBrain;

/// <summary>Why a neuron's activation ends.</summary>
public enum DeactivationCause
{
    /// <summary>The neuron or the runtime asked for it.</summary>
    Requested = 0,

    /// <summary>The neuron was idle.</summary>
    Idle = 1,

    /// <summary>The host is shutting down.</summary>
    Shutdown = 2,

    /// <summary>Activation or a turn failed.</summary>
    Failed = 3,
}
