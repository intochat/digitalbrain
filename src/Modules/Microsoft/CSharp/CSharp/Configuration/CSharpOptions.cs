namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpOptions
{
    public const string SectionName = "DigitalBrain:CSharp";

    // The repository the sandbox mounts at /brain; scripts compile against its contract projects.
    public string? SourceRoot { get; set; }
    // The IAspire neuron that starts the development sandbox and reports its URL.
    public string AspireApplication { get; set; } = "DigitalBrain";
    // The script edge URL runs dial; development derives it from this brain's own HTTP address.
    public string? EdgeUrl { get; set; }
    // Base64 HMAC key for run tokens, shared by every silo; development makes one per process.
    public string? RunTokenKey { get; set; }
    // Production: the Azure Container Apps session pool management endpoint. Set, it replaces the Aspire sandbox.
    public string? SessionPoolEndpoint { get; set; }
}
