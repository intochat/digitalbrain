namespace DigitalBrain.AI;

[GenerateSerializer, Alias("db.ai.provider-unavailable")]
public sealed class ProviderUnavailableException : InvalidOperationException
{
    public ProviderUnavailableException(string integration, string status, string[] missing)
        : base($"Integration '{integration}' is {status}"
            + (missing.Length == 0 ? "" : $"; missing {string.Join(", ", missing)}")
            + $". An operator completes it through POST /integrations/{integration}/registration.")
    {
        Integration = integration;
        Status = status;
        Missing = missing;
    }

    [Id(0)] public string Integration { get; }

    [Id(1)] public string Status { get; }

    [Id(2)] public string[] Missing { get; }
}

