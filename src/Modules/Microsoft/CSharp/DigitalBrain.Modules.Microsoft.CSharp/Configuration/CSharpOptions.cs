using System.Text.Json.Serialization;
using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpOptions : IModuleOptions
{
    // The deployment owns EdgeUrl, RunTokenKey and SessionPoolEndpoint on this section's configuration path; they never travel as module options.
    public const string SectionName = "DigitalBrain:CSharp";

    // The repository the sandbox mounts at /brain; scripts compile against its contract projects.
    public string? SourceRoot { get; set; }
    // The IAspire neuron that starts the development sandbox and reports its URL.
    public string AspireApplication { get; set; } = "DigitalBrain";
    // The script edge URL runs dial; development derives it from this brain's own HTTP address.
    [JsonIgnore]
    public string? EdgeUrl { get; set; }
    // Base64 HMAC key for run tokens, shared by every silo; development makes one per process.
    [JsonIgnore]
    public string? RunTokenKey { get; set; }
    // Production: the Azure Container Apps session pool management endpoint. Set, it replaces the Aspire sandbox.
    [JsonIgnore]
    public string? SessionPoolEndpoint { get; set; }

    public CSharpOptions WithSandbox(string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        SourceRoot = Path.GetFullPath(sourceRoot);
        return this;
    }

    public void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(AspireApplication);
}
