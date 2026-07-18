namespace DigitalBrain.Runtime.Ai;

public interface IVoiceModel
{
    static abstract string Id { get; }
    static abstract string DisplayName { get; }
    static abstract string Icon { get; }
    static abstract string ModelFileName { get; }
    static abstract string? ModelFileSha256 { get; }
}
