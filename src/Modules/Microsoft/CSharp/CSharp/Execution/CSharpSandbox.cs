namespace DigitalBrain.Microsoft.CSharp;

public static class CSharpSandbox
{
    public const string ResourceName = "csharp-sandbox";
    public const int Port = 8080;
    // The sandbox mounts the repository here, so scripts reference contracts by #:project path.
    public const string SourceMount = "/brain";
    public const string SourceProject = "src/Modules/Microsoft/CSharp/Sandbox";
}
