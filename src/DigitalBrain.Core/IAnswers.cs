namespace DigitalBrain;

public interface IAnswers<in TQuestion, TReply>
    where TQuestion : Synapse
    where TReply : Synapse
{
    Task<TReply?> HandleAsync(TQuestion question, CancellationToken cancellationToken);
}
