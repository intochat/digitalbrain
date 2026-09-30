using System.Text.Json.Serialization;
using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.Aspire;

public sealed class AspireOptions : IModuleOptions
{
    public const string SectionName = AspireModule.ConfigurationRoot;
    public string ApplicationName { get; set; } = "DigitalBrain";
    // The AppHost projects it at launch on the section's configuration path; it never travels as a module option.
    [JsonIgnore]
    public string? BridgeKey { get; set; }

    public void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(ApplicationName);
}
