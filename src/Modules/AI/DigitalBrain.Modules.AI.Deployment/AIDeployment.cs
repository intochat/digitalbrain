using System.Text.RegularExpressions;
using DigitalBrain.Deployment;

namespace DigitalBrain.AI;

// Provider API keys are manifest parameters and reach Key Vault with the rest; what this module deploys
// is the Ollama server local models run on, pulling every model the manifest names before serving.
public sealed partial class AIDeployment : IDigitalBrainModuleDeployment
{
    private const string Ollama = "ollama";
    private const int Port = 11434;

    public void Deploy(ModuleDeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Manifest.ResourceNames.Contains(Ollama)) { return; }
        var pulls = string.Concat(context.Manifest.ResourceNames
            .Where(resource => context.Manifest.TypeOf(resource) == "value.v0")
            .Select(resource => ModelReference().Match(context.Manifest.Resource(resource).GetProperty("connectionString").GetString() ?? ""))
            .Where(match => match.Success)
            .Select(match => match.Groups["model"].Value)
            .Distinct(StringComparer.Ordinal)
            .Select(model => $"; ollama pull {model} || echo \"pulling {model} failed\""));
        var app = context.AddService(new ServiceDefinition(Ollama, context.Manifest.Image(Ollama), Port)
        {
            Transport = "http",
            AllowInsecure = true,
            Command = ["/bin/sh", "-c"],
            // Pull once the server answers, each model on its own so one failure does not skip the rest.
            Arguments = [$"ollama serve & until ollama list >/dev/null 2>&1; do sleep 1; done{pulls}; wait"],
            DataPath = "/root/.ollama",
            DataQuotaGb = 64,
            Cpu = 4,
            Memory = "8Gi",
        });
        context.Provide(Ollama, "bindings.http.scheme", "http");
        context.Provide(Ollama, "bindings.http.host", context.InternalHost(app));
        context.Provide(Ollama, "bindings.http.port", "80");
    }

    [GeneratedRegex(@"^\{ollama\.connectionString\};Model=(?<model>[^;]+)$")]
    private static partial Regex ModelReference();
}
