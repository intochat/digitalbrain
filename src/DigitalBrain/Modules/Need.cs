using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain;

/// <summary>What kind of runtime resource a <see cref="Need"/> names.</summary>
public enum NeedKind
{
    /// <summary>A named durable state store (<see cref="Need.State"/>).</summary>
    State,

    /// <summary>Signal streams (<see cref="Need.Streams"/>).</summary>
    Streams,

    /// <summary>Durable reminders (<see cref="Need.Reminders"/>).</summary>
    Reminders,

    /// <summary>Encrypted secrets (<see cref="Need.Secrets"/>).</summary>
    Secrets,

    /// <summary>Outbound HTTP or inbound webhook ingress (<see cref="Need.Http"/>, <see cref="Need.HttpClient"/>, <see cref="Need.Webhook"/>).</summary>
    Http,

    /// <summary>Isolated code execution or container sidecar (<see cref="Need.Sandbox"/>, <see cref="Need.Container"/>).</summary>
    Sandbox,

    /// <summary>A language model (<see cref="Need.Model"/>).</summary>
    Model,
}

/// <summary>A runtime resource a module needs from the host.</summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Need is the domain term for a module's runtime dependency.")]
public sealed record Need
{
    private Need(NeedKind kind, string? name)
    {
        Kind = kind;
        Name = name;
    }

    /// <summary>Which resource kind this is.</summary>
    public NeedKind Kind { get; }

    /// <summary>The store, client, webhook path, or container name when applicable; <see langword="null"/> for unnamed needs.</summary>
    public string? Name { get; }

    /// <summary>Optional setting section or key holding the base address for <see cref="Need.HttpClient"/>.</summary>
    public string? BaseAddressSetting { get; init; }

    /// <summary>Optional rate limit in requests per minute for <see cref="Need.HttpClient"/>.</summary>
    public int? RequestsPerMinute { get; init; }

    /// <summary>The target neuron type alias for <see cref="Need.Webhook"/>.</summary>
    public string? TargetNeuronType { get; init; }

    /// <summary>The container image for <see cref="Need.Container"/>.</summary>
    public string? Image { get; init; }

    /// <summary>Signal streams.</summary>
    public static Need Streams { get; } = new(NeedKind.Streams, null);

    /// <summary>Durable reminders.</summary>
    public static Need Reminders { get; } = new(NeedKind.Reminders, null);

    /// <summary>Encrypted secrets.</summary>
    public static Need Secrets { get; } = new(NeedKind.Secrets, null);

    /// <summary>Outbound HTTP.</summary>
    public static Need Http { get; } = new(NeedKind.Http, null);

    /// <summary>Isolated code execution.</summary>
    public static Need Sandbox { get; } = new(NeedKind.Sandbox, null);

    /// <summary>A language model.</summary>
    public static Need Model { get; } = new(NeedKind.Model, null);

    /// <summary>A named durable state store.</summary>
    public static Need State(string name) => new(NeedKind.State, Names.Require(name, nameof(name), "state store"));

    /// <summary>A named metered and rate-limited HTTP client pipeline.</summary>
    public static Need HttpClient(string name, string? baseAddressSetting = null, int requestsPerMinute = 120)
        => new(NeedKind.Http, Names.Require(name, nameof(name), "http client"))
        {
            BaseAddressSetting = baseAddressSetting,
            RequestsPerMinute = requestsPerMinute,
        };

    /// <summary>A declarative webhook ingress endpoint that dispatches a signal to a target neuron.</summary>
    public static Need Webhook(string path, string targetNeuronType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNeuronType);
        return new(NeedKind.Http, path)
        {
            TargetNeuronType = targetNeuronType,
        };
    }

    /// <summary>A declarative container sidecar resource.</summary>
    public static Need Container(string name, string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        return new(NeedKind.Sandbox, Names.Require(name, nameof(name), "container"))
        {
            Image = image,
        };
    }

    /// <inheritdoc />
    public override string ToString() => Name is null ? Kind.ToString() : $"{Kind}({Name})";
}
