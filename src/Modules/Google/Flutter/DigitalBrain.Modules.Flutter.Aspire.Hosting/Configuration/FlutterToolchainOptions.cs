using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Flutter.Aspire.Hosting;

// The legacy flat command keys remain supported alongside FlutterHostOptions.
internal sealed class FlutterToolchainOptions
{
    public string? FlutterCommand { get; set; }

    internal static FlutterToolchainOptions Read(IConfiguration? configuration)
        => configuration?.GetSection("DigitalBrain").Get<FlutterToolchainOptions>() ?? new();
}
