namespace DigitalBrain.Platform.Identity.Configuration;

public sealed class IdentityHostOptions
{
    public const string SectionName = "DigitalBrain:Identity";

    public string CookieName { get; set; } = "digitalbrain.session";
    public string ProtectionApplicationName { get; set; } = "DigitalBrain.v1";
    public string ProtectionContainerName { get; set; } = "digitalbrain-protection-v1";

    public bool Validate() => !string.IsNullOrWhiteSpace(CookieName)
        && !string.IsNullOrWhiteSpace(ProtectionApplicationName)
        && !string.IsNullOrWhiteSpace(ProtectionContainerName);
}
