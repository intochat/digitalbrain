using DigitalBrain.Kernel.Uploads;
using Microsoft.Data.Sqlite;

namespace DigitalBrain.Tests.Uploads;

public class ChatUploadClassifierTests
{
    [Theory]
    [InlineData("budget.db")]
    [InlineData("budget.sqlite")]
    [InlineData("budget.sqlite3")]
    public void Classify_Detects_Sqlite_Database_Uploads(string fileName)
    {
        Assert.Equal(ChatUploadKind.SqliteDatabase, ChatUploadClassifier.Classify(fileName));
    }

    [Fact]
    public void Classify_Keeps_Xlsx_On_Tabular_Path()
    {
        Assert.Equal(ChatUploadKind.TabularWorkbook, ChatUploadClassifier.Classify("q2-sales.xlsx"));
    }

    [Fact]
    public void BuildDbInspectSchema_Uses_Temp_Path_For_Connection_And_FileName_For_Source()
    {
        var cmd = ChatUploadClassifier.BuildDbInspectSchema(
            @"C:\Users\demo\budget.db",
            @"C:\Temp\upload-copy.db",
            "session-1");

        var builder = new SqliteConnectionStringBuilder(cmd.ConnectionString);

        Assert.Equal("budget", cmd.ConnectionName);
        Assert.Equal("sqlite", cmd.Provider);
        Assert.Equal("budget.db", cmd.SourcePath);
        Assert.Equal("session-1", cmd.SessionId);
        Assert.Equal(@"C:\Temp\upload-copy.db", builder.DataSource);
    }
}
