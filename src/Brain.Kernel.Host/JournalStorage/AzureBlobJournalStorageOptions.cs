namespace Brain.Kernel.Host.JournalStorage;

public sealed class AzureBlobJournalStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "journals";

    public string JournalFormatKey { get; set; } = "json";

    public int CompactionSegmentThreshold { get; set; } = 10;
}
