namespace DigitalBrain;

public sealed class AskOutcomeUnknownException : Exception
{
    internal AskOutcomeUnknownException(NeuronId session, Exception wireFailure)
        : base($"The ask wire call on session {session} failed before an outcome was observed; "
            + "read the session journal to learn whether the ask committed — never refire "
            + "(a second fire would journal a second ask).", wireFailure)
        => Session = session;

    public NeuronId Session { get; }
}
