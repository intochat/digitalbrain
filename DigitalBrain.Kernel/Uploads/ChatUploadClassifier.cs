using DigitalBrain.Core;
using Microsoft.Data.Sqlite;

namespace DigitalBrain.Kernel.Uploads;

public enum ChatUploadKind
{
    Unsupported,
    TabularWorkbook,
    SqliteDatabase
}

public static class ChatUploadClassifier
{
    public static ChatUploadKind Classify(string fileName)
    {
        if (IsSqliteDatabase(fileName))
            return ChatUploadKind.SqliteDatabase;

        if (IsTabularWorkbook(fileName))
            return ChatUploadKind.TabularWorkbook;

        return ChatUploadKind.Unsupported;
    }

    public static bool IsSqliteDatabase(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTabularWorkbook(string fileName) =>
        Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    public static DbInspectSchema BuildDbInspectSchema(
        string fileName,
        string serverPath,
        string? clientId,
        string? workspaceId = null)
    {
        var safeFileName = SafeFileName(fileName);
        var connectionName = Path.GetFileNameWithoutExtension(safeFileName);
        if (string.IsNullOrWhiteSpace(connectionName))
            connectionName = "sqlite-upload";

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = serverPath
        }.ToString();

        return new DbInspectSchema(
            connectionName,
            "sqlite",
            connectionString,
            safeFileName,
            clientId,
            workspaceId);
    }

    public static string TempDatabasePath(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !IsSqliteDatabase("x" + extension))
            extension = ".db";

        return Path.Combine(Path.GetTempPath(), "digitalbrain-upload-" + Guid.NewGuid().ToString("N") + extension);
    }

    private static string SafeFileName(string fileName)
    {
        try
        {
            var safe = Path.GetFileName(fileName.Replace('\\', Path.DirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(safe) ? "database.db" : safe;
        }
        catch (ArgumentException)
        {
            return "database.db";
        }
    }
}
