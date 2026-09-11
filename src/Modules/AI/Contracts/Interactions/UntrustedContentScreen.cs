namespace DigitalBrain.AI.Interactions;

public interface IUntrustedContentScreen
{
    Task ScreenAsync(string content, CancellationToken cancellationToken);
}
