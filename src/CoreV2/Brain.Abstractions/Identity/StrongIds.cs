namespace Brain.Abstractions.Identity;

public readonly record struct WorkspaceId
{
    public WorkspaceId(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public static WorkspaceId Empty => default;

    public override string ToString() => Value ?? string.Empty;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct PrincipalId
{
    public PrincipalId(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct BrainActivityId
{
    public BrainActivityId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A brain activity id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static BrainActivityId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}

public readonly record struct ModuleId
{
    public ModuleId(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct OperationId
{
    public OperationId(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct CapabilityId
{
    public CapabilityId(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct NeuronRoleId
{
    public NeuronRoleId(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct SynapseKey
{
    public SynapseKey(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A synapse key cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static SynapseKey New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}

public readonly record struct WiringId
{
    public WiringId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A wiring id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static WiringId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}

public readonly record struct CorrelationId
{
    public CorrelationId(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct IdempotencyKey
{
    public IdempotencyKey(string value) => Value = Required(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
