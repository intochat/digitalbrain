using System.Globalization;
using DigitalBrain.Core;

namespace DigitalBrain.Behavior;

internal sealed record BehaviorSandboxRequest(
    string Image,
    string ContainerName,
    string ArtifactDirectory,
    string EntryAssembly,
    int HostControlPort,
    IReadOnlyDictionary<string, string> Environment);

public static class BehaviorSandbox
{
    public const string ImageName = "digitalbrain-behavior-sandbox:local";

    internal static IReadOnlyList<string> Arguments(BehaviorSandboxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsImageReference(request.Image))
        {
            throw new ArgumentException("Sandbox image reference is not a local image name.", nameof(request));
        }

        if (!IsContainerName(request.ContainerName))
        {
            throw new ArgumentException("Sandbox container name is not a single docker name.", nameof(request));
        }

        if (!Path.IsPathRooted(request.ArtifactDirectory))
        {
            throw new ArgumentException("Behavior artifact directory must be absolute.", nameof(request));
        }

        if (!IsEntryAssembly(request.EntryAssembly))
        {
            throw new ArgumentException("Behavior entry must be a file name inside the artifact.", nameof(request));
        }

        if (request.HostControlPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var arguments = new List<string>
        {
            "run", "--rm", "--name", request.ContainerName,
            "--read-only",
            "--cap-drop=ALL",
            "--security-opt=no-new-privileges",
            "--pids-limit=128",
            "--memory=512m",
            "--cpus=1",
            "--tmpfs", "/tmp:rw,noexec,nosuid,size=64m",
            "-p", string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{request.HostControlPort}:{BehaviorSandboxControl.Port}"),
            "-v", $"{request.ArtifactDirectory}:/behavior:ro",
            "-w", "/behavior",
            "-e", "HOME=/tmp",
            "-e", "DOTNET_EnableDiagnostics=0",
        };
        foreach (var item in request.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!IsEnvironmentName(item.Key) || item.Value.Contains('\n') || item.Value.Contains('\r'))
            {
                throw new ArgumentException("Sandbox environment is not a single-line variable.", nameof(request));
            }

            arguments.Add("-e");
            arguments.Add(item.Key + "=" + RewriteLoopback(item.Key, item.Value));
        }

        arguments.Add(request.Image);
        arguments.Add(request.EntryAssembly);
        return arguments;
    }

    internal static string RewriteLoopback(string name, string value)
    {
        if (!string.Equals(name, "Gateways", StringComparison.Ordinal))
        {
            return value;
        }

        return string.Join(';', value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(RewriteGateway));
    }

    private static string RewriteGateway(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host is not ("127.0.0.1" or "localhost" or "::1"))
        {
            return value;
        }

        return new UriBuilder(uri) { Host = "host.docker.internal" }.Uri.ToString();
    }

    private static bool IsImageReference(string image)
        => image.Length is > 0 and <= 200
            && image.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '/' or '.' or '-' or '_');

    private static bool IsContainerName(string name)
        => name.Length is > 0 and <= 63
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsEntryAssembly(string name)
        => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && name.IndexOfAny(['/', '\\', ':']) < 0
            && !name.Contains("..", StringComparison.Ordinal)
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool IsEnvironmentName(string name)
        => name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
