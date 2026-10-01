namespace DigitalBrain.Identity;

public sealed class KernelCorsOptions
{
    public const string SectionName = "DigitalBrain:Cors";
    public string? AllowedOrigin { get; set; }
}
