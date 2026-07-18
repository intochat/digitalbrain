namespace DigitalBrain.Runtime.User;

public interface IUser
{
    IDomainDiscovery GetDomains { get; }
}

public interface IDomainDiscovery
{
    IReadOnlyList<SearchResult> Search(string prompt);
}

public sealed record SearchResult(
    string FilePath,
    string Domain,
    IReadOnlyList<string> Neurons,
    IReadOnlyList<string> Synapses,
    double Score = 0);
