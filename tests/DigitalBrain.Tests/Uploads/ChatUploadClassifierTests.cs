using DigitalBrain.Kernel.Uploads;

namespace DigitalBrain.Tests.Uploads;

public class ChatUploadClassifierTests
{
    [Fact]
    public void Classify_Keeps_Xlsx_On_Tabular_Path()
    {
        Assert.Equal(ChatUploadKind.TabularWorkbook, ChatUploadClassifier.Classify("q2-sales.xlsx"));
    }

    [Fact]
    public void Classify_Unsupported_For_Other_Extensions()
    {
        Assert.Equal(ChatUploadKind.Unsupported, ChatUploadClassifier.Classify("notes.txt"));
        Assert.Equal(ChatUploadKind.Unsupported, ChatUploadClassifier.Classify("data.db"));
    }
}
