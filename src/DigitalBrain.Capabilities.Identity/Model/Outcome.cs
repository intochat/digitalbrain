namespace DigitalBrain.Capabilities.Identity.Model;

using System.Collections.Immutable;
using DigitalBrain;

/// <summary>Why a directory transition was refused by the aggregate.</summary>
public enum Refusal
{
    /// <summary>The transition was accepted.</summary>
    None = 0,

    /// <summary>The caller is not the owner of the target brain.</summary>
    NotOwner = 1,

    /// <summary>The brain owner cannot be removed.</summary>
    OwnerStays = 2,
}

/// <summary>The outcome of an aggregate transition.</summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <param name="Next">The next state.</param>
/// <param name="Facts">The emitted domain facts.</param>
/// <param name="Refused">The refusal reason, or <see cref="Refusal.None"/> if accepted.</param>
public readonly record struct Outcome<TState>(TState Next, ImmutableArray<Signal> Facts, Refusal Refused = Refusal.None) where TState : class
{
    /// <summary>Whether the state changed without refusal.</summary>
    public bool Changed => Refused == Refusal.None && Facts.Length > 0;

    /// <summary>Returns the next state or throws an exception matching the refusal.</summary>
    /// <param name="by">The calling capability caller.</param>
    /// <param name="brainId">The target brain identifier.</param>
    /// <returns>The next state.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if <see cref="Refused"/> is <see cref="Refusal.NotOwner"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Refused"/> is <see cref="Refusal.OwnerStays"/>.</exception>
    public TState OrThrow(CapabilityCaller by, string brainId)
    {
        ArgumentNullException.ThrowIfNull(by);
        if (Refused == Refusal.NotOwner)
        {
            throw new UnauthorizedAccessException($"{by.PrincipalId} is not the owner of {brainId}.");
        }

        if (Refused == Refusal.OwnerStays)
        {
            throw new InvalidOperationException($"Owner of {brainId} cannot be removed.");
        }

        return Next;
    }
}
