namespace DigitalBrain.Platform.Secrets;

public sealed class MasterKeyOptions
{
    public const string SectionName = "DigitalBrain";
    public string MasterKey { get; set; } = "";
}
