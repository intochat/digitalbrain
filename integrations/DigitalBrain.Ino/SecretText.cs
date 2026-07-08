using DigitalBrain.Core;

namespace DigitalBrain.Ino;

public static class SecretText
{
    public static string Redact(string value) => SensitiveText.Redact(value);
}
