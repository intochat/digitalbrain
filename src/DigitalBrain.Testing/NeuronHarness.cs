using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

/// <summary>Creates neurons attached to a <see cref="FakeNeuronContext"/>.</summary>
public static class NeuronHarness
{
    /// <summary>Creates the neuron, attaches a fake context and runs its activation.</summary>
    public static async Task<NeuronHarness<TNeuron>> Create<TNeuron>(NeuronId id, CancellationToken cancellationToken = default)
        where TNeuron : Neuron, new()
    {
        var neuron = new TNeuron();
        var context = new FakeNeuronContext(id);
        NeuronAttachment.Attach(neuron, context);
        context.Reminders.Target = neuron;
        var harness = new NeuronHarness<TNeuron>(neuron, context);
        await NeuronAttachment.Activate(neuron, cancellationToken).ConfigureAwait(false);
        return harness;
    }
}

/// <summary>A neuron and the fake context it is attached to.</summary>
public sealed class NeuronHarness<TNeuron> where TNeuron : Neuron
{
    internal NeuronHarness(TNeuron neuron, FakeNeuronContext context)
    {
        Neuron = neuron;
        Context = context;
    }

    /// <summary>The neuron under test.</summary>
    public TNeuron Neuron { get; }

    /// <summary>The fake context.</summary>
    public FakeNeuronContext Context { get; }

    /// <summary>Runs the neuron's deactivation.</summary>
    public Task Deactivate(DeactivationCause cause = DeactivationCause.Requested, CancellationToken cancellationToken = default)
        => NeuronAttachment.Deactivate(Neuron, cause, cancellationToken);

    /// <summary>Passes when a published signal of the type matches; otherwise fails listing what was published.</summary>
    /// <exception cref="NeuronAssertionException">No published signal matches.</exception>
    public TSignal AssertPublished<TSignal>(Func<TSignal, bool>? predicate = null) where TSignal : Signal
    {
        var match = Context.Published.OfType<TSignal>().FirstOrDefault(signal => predicate?.Invoke(signal) ?? true);
        return match ?? throw new NeuronAssertionException(
            $"No published {typeof(TSignal).Name} matched. Published: [{string.Join(", ", Context.Published)}]");
    }

    /// <summary>Passes when nothing was published.</summary>
    /// <exception cref="NeuronAssertionException">Something was published.</exception>
    public void AssertNothingPublished()
    {
        if (Context.Published.Count > 0)
        {
            throw new NeuronAssertionException($"Expected no signals. Published: [{string.Join(", ", Context.Published)}]");
        }
    }
}

/// <summary>A failed assertion of <see cref="NeuronHarness{TNeuron}"/>.</summary>
public sealed class NeuronAssertionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public NeuronAssertionException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public NeuronAssertionException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    public NeuronAssertionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
