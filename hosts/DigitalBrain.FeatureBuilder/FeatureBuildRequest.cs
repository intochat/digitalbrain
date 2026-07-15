using System.Collections.ObjectModel;
using System.Text;
namespace DigitalBrain.FeatureBuilder;

public sealed class FeatureSourceFile
{
    public FeatureSourceFile(string path, string content)
    {
        Path = FeatureSourceSnapshot.ValidatePath(path, nameof(path));
        ArgumentNullException.ThrowIfNull(content);
        var byteCount = Encoding.UTF8.GetByteCount(content);
        if (byteCount > FeatureSourceSnapshot.MaximumFileBytes)
        {
            throw new ArgumentException($"A source file cannot exceed {FeatureSourceSnapshot.MaximumFileBytes} UTF-8 bytes.", nameof(content));
        }
        if (content.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Source files cannot contain null characters.", nameof(content));
        }
        Content = content;
        Utf8ByteCount = byteCount;
    }
    public FeatureSourceFile(string path, byte[] content)
        : this(path, Decode(content))
    {
    }
    public string Path { get; }
    public string Content { get; }
    public int Utf8ByteCount { get; }
    private static string Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > FeatureSourceSnapshot.MaximumFileBytes)
        {
            throw new ArgumentException($"A source file cannot exceed {FeatureSourceSnapshot.MaximumFileBytes} UTF-8 bytes.", nameof(content));
        }
        try
        {
            return new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("Source files must contain valid UTF-8.", nameof(content), exception);
        }
    }
}
public sealed class FeatureSourceSnapshot
{
    private static readonly char[] InvalidPathCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> ReservedPathSegments = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"],
        StringComparer.OrdinalIgnoreCase);
    public const int MaximumFileCount = 64;
    public const int MaximumFileBytes = 1_048_576;
    public const int MaximumTotalBytes = 4_194_304;
    public const int MaximumPathLength = 240;
    public FeatureSourceSnapshot(string implementationProjectPath, string scenarioProjectPath, IReadOnlyList<FeatureSourceFile> files)
    {
        ImplementationProjectPath = ValidatePath(implementationProjectPath, nameof(implementationProjectPath));
        ScenarioProjectPath = ValidatePath(scenarioProjectPath, nameof(scenarioProjectPath));
        if (!ImplementationProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            !ScenarioProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Implementation and scenario entries must be C# projects.");
        }
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0 || files.Count > MaximumFileCount)
        {
            throw new ArgumentException($"A source snapshot must contain 1 to {MaximumFileCount} files.", nameof(files));
        }
        var copy = new FeatureSourceFile[files.Count];
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index] ?? throw new ArgumentException("Source snapshots cannot contain null files.", nameof(files));
            if (!paths.Add(file.Path))
            {
                throw new ArgumentException($"Duplicate source path '{file.Path}'.", nameof(files));
            }
            totalBytes = checked(totalBytes + file.Utf8ByteCount);
            if (totalBytes > MaximumTotalBytes)
            {
                throw new ArgumentException($"A source snapshot cannot exceed {MaximumTotalBytes} UTF-8 bytes.", nameof(files));
            }
            copy[index] = file;
        }
        if (!paths.Contains(ImplementationProjectPath) || !paths.Contains(ScenarioProjectPath))
        {
            throw new ArgumentException("Both entry projects must exist in the source snapshot.", nameof(files));
        }
        Files = new ReadOnlyCollection<FeatureSourceFile>(copy);
    }
    public string ImplementationProjectPath { get; }
    public string ScenarioProjectPath { get; }
    public IReadOnlyList<FeatureSourceFile> Files { get; }
    internal static string ValidatePath(string path, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(path, parameterName);
        if (path.Length is 0 or > MaximumPathLength || path.Contains('\\', StringComparison.Ordinal) ||
            path.StartsWith('/', StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') ||
            path.Split('/').Any(static segment => !IsPortablePathSegment(segment)))
        {
            throw new ArgumentException("A bounded canonical relative path is required.", parameterName);
        }
        return path;
    }

    private static bool IsPortablePathSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." ||
            !string.Equals(segment, segment.Trim(), StringComparison.Ordinal) ||
            segment.Any(char.IsControl) ||
            segment.IndexOfAny(InvalidPathCharacters) >= 0 ||
            segment.EndsWith('.'))
            return false;
        return !ReservedPathSegments.Contains(segment.Split('.', 2)[0]);
    }
}
public sealed class FeatureBuildRequest
{
    public FeatureBuildRequest(FeatureSourceSnapshot source, string offlineFeedDirectory, string outputDirectory, DateTimeOffset deadline)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        OfflineFeedDirectory = ExistingDirectory(offlineFeedDirectory, nameof(offlineFeedDirectory));
        OutputDirectory = FullPath(outputDirectory, nameof(outputDirectory));
        if (PathsOverlap(OfflineFeedDirectory, OutputDirectory))
        {
            throw new ArgumentException("The package feed and release output must not overlap.");
        }
        Deadline = deadline;
    }
    public FeatureSourceSnapshot Source { get; }
    public string OfflineFeedDirectory { get; }
    public string OutputDirectory { get; }
    public DateTimeOffset Deadline { get; }
    private static string ExistingDirectory(string path, string parameterName)
    {
        var fullPath = FullPath(path, parameterName);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory '{fullPath}' does not exist.");
        }
        return fullPath;
    }
    private static string FullPath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static bool PathsOverlap(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var firstPrefix = first + Path.DirectorySeparatorChar;
        var secondPrefix = second + Path.DirectorySeparatorChar;
        return string.Equals(first, second, comparison) || firstPrefix.StartsWith(secondPrefix, comparison) ||
            secondPrefix.StartsWith(firstPrefix, comparison);
    }
}
public enum FeatureBuildFailure
{
    InvalidSource,
    ForbiddenPackage,
    RestoreFailed,
    CompilationFailed,
    NondeterministicInput,
    ScenarioFailed,
    DeadlineExceeded,
    ReleaseConflict
}
public sealed class FeatureBuildException : Exception
{
    public FeatureBuildException(FeatureBuildFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }
    public FeatureBuildException(FeatureBuildFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }
    public FeatureBuildFailure Failure { get; }
}
