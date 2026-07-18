namespace DigitalBrain.Abstractions.Ino;

public interface IInoSession : IGrainWithGuidKey
{
    Task StartAsync(InoSessionOptions options);
    Task<InoSessionInfo> GetInfoAsync();
    Task NotifyNeedsInputAsync(string prompt, string reason);
}
