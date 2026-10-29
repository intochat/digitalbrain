using System.Text.Json.Serialization;

namespace DigitalBrain.Operations;

/// <summary>The id of a running or finished operation.</summary>
/// <param name="Value">The opaque id.</param>
public readonly record struct OperationId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>What an operation will do. Concrete plans (copy, move, write) derive from it.</summary>
/// <param name="DryRun">When true the operation only plans and completes with <see cref="OperationResult.Planned"/>.</param>
public abstract record OperationPlan(bool DryRun);

/// <summary>How an operation ended. Persisted and journaled as JSON with a <c>$kind</c> discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(Succeeded), "succeeded")]
[JsonDerivedType(typeof(Failed), "failed")]
[JsonDerivedType(typeof(Cancelled), "cancelled")]
[JsonDerivedType(typeof(Planned), "planned")]
public abstract record OperationResult
{
    private OperationResult() { }

    /// <summary>The operation did everything it planned.</summary>
    public sealed record Succeeded : OperationResult;

    /// <summary>The operation stopped on an error.</summary>
    /// <param name="Code">A stable error code (for example <c>conflict</c>).</param>
    /// <param name="Message">A human-readable message.</param>
    public sealed record Failed(string Code, string Message) : OperationResult;

    /// <summary>The operation was cancelled.</summary>
    public sealed record Cancelled : OperationResult;

    /// <summary>A dry run finished; these are the steps a real run would take.</summary>
    /// <param name="Steps">The planned steps, in order.</param>
    public sealed record Planned(IReadOnlyList<string> Steps) : OperationResult;
}
