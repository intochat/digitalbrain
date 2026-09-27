namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpOptions
{
    public const string SectionName = "DigitalBrain:CSharp";

    // The repository the sandbox mounts at /brain; scripts compile against its contract projects.
    public string? SourceRoot { get; set; }
    // The IAspire neuron that starts the sandbox and reports its URL.
    public string AspireApplication { get; set; } = "DigitalBrain";
}
