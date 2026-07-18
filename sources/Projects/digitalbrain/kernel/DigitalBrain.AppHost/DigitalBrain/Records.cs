namespace DigitalBrain.Hosting.DigitalBrain;

public sealed record DeclaredEmbeddingModel(string Id, string Provider, string DisplayName, string Icon, int Dimensions);

public sealed record DeclaredVoiceModel(string Id, string DisplayName, string Icon, string ModelFileName, string? ModelFileSha256);
