using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.RuntimeMigration;

public sealed record LegacyJournalPaths(
    string Root,
    string Operations,
    string Conversations)
{
    private static readonly string[] LegacyFileNames =
    [
        "operations.jsonl",
        "operations.jsonl.ino-effects",
        "sessions.jsonl",
        "projections.jsonl",
        "ui-feed.jsonl"
    ];

    public LegacyJournalLease AcquireExclusive()
    {
        var paths = LegacyFileNames.Select(fileName => Path.Combine(Root, fileName))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var streams = new List<FileStream>(paths.Length);
        try
        {
            foreach (var path in paths) streams.Add(Acquire(path));
            return new LegacyJournalLease(streams);
        }
        catch
        {
            foreach (var stream in streams) stream.Dispose();
            throw new MigrationGapException("legacy-source-busy");
        }
    }

    public void FreezeAll()
    {
        foreach (var fileName in LegacyFileNames)
        {
            Freeze(Path.Combine(Root, fileName));
            Freeze(Path.Combine(Root, fileName) + ".head");
            Freeze(Path.Combine(Root, fileName) + ".pending");
            Freeze(Path.Combine(Root, fileName) + ".quarantine");
        }
    }

    private static FileStream Acquire(string path)
    {
        var lockPath = Path.GetFullPath(path) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The legacy journal lock is unavailable.");
        var stopwatch = Stopwatch.StartNew();
        IOException? lastError = null;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(10);
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = new IOException("The legacy journal lock is unavailable.", exception);
                break;
            }
        }
        throw new IOException("The legacy journal lock is unavailable.", lastError);
    }

    private static void Freeze(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new MigrationGapException("legacy-source-link-invalid");
            if ((attributes & FileAttributes.ReadOnly) == 0)
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        }
        catch (IOException)
        {
            throw new MigrationGapException("legacy-freeze-failed");
        }
        catch (UnauthorizedAccessException)
        {
            throw new MigrationGapException("legacy-freeze-failed");
        }
    }
}

public sealed class LegacyJournalLease(IReadOnlyList<FileStream> streams) : IDisposable
{
    public void Dispose()
    {
        for (var index = streams.Count - 1; index >= 0; index--) streams[index].Dispose();
    }
}

public static class LegacyDataRootLocator
{
    public static LegacyJournalPaths Locate(IConfiguration configuration)
    {
        var profile = configuration["DigitalBrain:Profile"] ?? "Development";
        if (!profile.Equals("Development", StringComparison.OrdinalIgnoreCase) &&
            !profile.Equals("Test", StringComparison.OrdinalIgnoreCase))
            throw new MigrationGapException("legacy-profile-not-local");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new MigrationGapException("local-application-data-unavailable");

        var repository = FindRepositoryRoot(Environment.CurrentDirectory) ??
                         FindRepositoryRoot(AppContext.BaseDirectory) ??
                         throw new MigrationGapException("repository-root-unavailable");
        var appHostDirectory = Path.GetFullPath(Path.Combine(repository, "hosts", "DigitalBrain.AppHost"));
        if (!Directory.Exists(appHostDirectory))
            throw new MigrationGapException("apphost-directory-unavailable");
        var canonicalPath = OperatingSystem.IsWindows()
            ? appHostDirectory.ToUpperInvariant()
            : appHostDirectory;
        var scope = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)))[..16];
        var root = Path.Combine(localAppData, "DigitalBrain", "V2", scope, profile.ToLowerInvariant());
        if (!Directory.Exists(root)) throw new MigrationGapException("legacy-source-missing");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new MigrationGapException("legacy-source-link-invalid");
        return new LegacyJournalPaths(
            root,
            Path.Combine(root, "operations.jsonl"),
            Path.Combine(root, "operations.jsonl.ino-effects"));
    }

    private static string? FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(Path.GetFullPath(start));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Brain.slnx")) &&
                Directory.Exists(Path.Combine(current.FullName, "hosts", "DigitalBrain.AppHost")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }
}
