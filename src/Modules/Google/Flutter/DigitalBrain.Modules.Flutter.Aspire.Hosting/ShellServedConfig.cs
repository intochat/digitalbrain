using System.Text.Json;

namespace DigitalBrain.Flutter.Aspire.Hosting;

// The file an AppHost writes next to the containerized web bundle: the serve-time
// counterpart of the --dart-define values the flutter run path bakes into each compile.
// Keys are the host variable names so the shell resolves both sources identically.
internal static class ShellServedConfig
{
    public const string ContainerPath = "/usr/share/nginx/html/config.json";

    public static string Payload(string uiBase, string shell, string chat)
        => JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [ShellNames.UIBaseEnvironmentVariable] = uiBase,
            [ShellNames.ShellEnvironmentVariable] = shell,
            [ShellNames.ChatEnvironmentVariable] = chat,
        });
}
