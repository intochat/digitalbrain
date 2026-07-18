namespace DigitalBrain.Runtime.Runtime;

[GenerateSerializer]
public sealed class PersistedRfwCard
{
    [Id(0)] public string CorrelationId { get; set; } = "";
    [Id(1)] public string LibraryName { get; set; } = "";
    [Id(2)] public string RootWidget { get; set; } = "";
    [Id(3)] public string DataJson { get; set; } = "";
    [Id(4)] public string Timestamp { get; set; } = "";
    [Id(5)] public string CallerNeuronType { get; set; } = "";
}

[Alias("neuronrfwstore")]
public interface INeuronRfwStoreGrain : IGrainWithStringKey
{
    Task SaveLatestCardAsync(PersistedRfwCard card);
    Task<PersistedRfwCard?> GetLatestCardAsync();
}
