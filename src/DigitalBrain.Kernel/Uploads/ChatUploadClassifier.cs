namespace DigitalBrain.Kernel.Uploads;

public enum ChatUploadKind
{
    Unsupported,
    TabularWorkbook
}

public static class ChatUploadClassifier
{
    public static ChatUploadKind Classify(string fileName)
    {
        if (IsTabularWorkbook(fileName))
        {
            return ChatUploadKind.TabularWorkbook;
        }

        return ChatUploadKind.Unsupported;
    }

    public static bool IsTabularWorkbook(string fileName) =>
        Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
}
