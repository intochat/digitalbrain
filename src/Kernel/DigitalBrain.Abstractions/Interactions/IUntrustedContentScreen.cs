namespace DigitalBrain.Abstractions.Interactions;

public interface IUntrustedContentScreen
{
    Task ScreenAsync(string content, CancellationToken cancellationToken);
}
