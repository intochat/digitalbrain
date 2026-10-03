using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.Aspire;

public sealed class AspireOptions : IModuleOptions
{
    public string ApplicationName { get; set; } = "DigitalBrain";

    public void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(ApplicationName);
}
