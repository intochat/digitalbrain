namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.manifest-exception")]
public sealed class AppManifestException(string message) : Exception(message);
