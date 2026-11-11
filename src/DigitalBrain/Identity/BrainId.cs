using DigitalBrain.Brains;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain;

/// <summary>A brain's id. Every neuron lives in exactly one brain.</summary>
/// <remarks>
/// 1-200 characters, no control characters, no path separators and no ':'. A leading '@' marks a
/// system scope (<see cref="Kernel"/>); such ids come only from <see cref="System(string)"/>, so user
/// input never names a system scope.
/// </remarks>
public readonly record struct BrainId
{
    private static readonly SearchValues<char> DisallowedCompoundChars = SearchValues.Create("!?*<>|\"@");
    /// <summary>The reserved scope of neurons that belong to no person's brain.</summary>
    public static BrainId Kernel { get; } = System("kernel");

    /// <exception cref="ArgumentException"><paramref name="value"/> is not a brain id.</exception>
    public BrainId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException("A brain id has 1-200 characters without control characters, '/', '\\', ':' or a leading '@'.", nameof(value));
        }
        Value = value;
    }

    private BrainId(string value, bool system)
    {
        _ = system;
        Value = value;
    }

    /// <summary>The id text.</summary>
    public string Value { get; }

    /// <summary>True for a reserved system scope such as <see cref="Kernel"/>.</summary>
    public bool IsSystem => Value is ['@', ..];

    /// <summary>A reserved system scope, written '@name'.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a brain id.</exception>
    public static BrainId System(string name)
    {
        if (!IsValid(name) || name.Length > 199)
        {
            throw new ArgumentException("A system scope name follows the brain id rule.", nameof(name));
        }
        return new BrainId("@" + name, system: true);
    }

    /// <summary>The brain id rule, the same as the legacy NeuronAddress.IsBrainId.</summary>
    public static bool IsValid([NotNullWhen(true)] string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value[0] != '@'
            && !value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':');

    /// <summary>True when <paramref name="value"/> is either a valid user brain id or a valid <c>@system</c> brain id.</summary>
    internal static bool IsValidUserOrSystem([NotNullWhen(true)] string? value)
        => !string.IsNullOrWhiteSpace(value) && value[0] == '@'
            ? value.Length <= 200 && IsValid(value[1..]) && !value.AsSpan(1).ContainsAny(DisallowedCompoundChars)
            : IsValid(value) && !value.AsSpan().ContainsAny(DisallowedCompoundChars);

    /// <summary>Parses a user or <c>@system</c> brain id.</summary>
    internal static bool TryParseUserOrSystem([NotNullWhen(true)] string? value, out BrainId brain)
    {
        brain = default;
        if (!IsValidUserOrSystem(value))
        {
            return false;
        }

        brain = value[0] == '@' ? System(value[1..]) : new BrainId(value);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
