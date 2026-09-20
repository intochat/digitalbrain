using System.Collections.ObjectModel;

namespace DigitalBrain.Core;

public interface IApplicationConfiguration
{
    ApplicationConfigurationSnapshot CreateSnapshot();
}

public sealed class ApplicationConfigurationSnapshot
{
    public ApplicationConfigurationSnapshot(string application, IReadOnlyDictionary<string, string?> configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        Application = application;
        Configuration = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(configuration, StringComparer.OrdinalIgnoreCase));
    }
    public string Application { get; }
    public IReadOnlyDictionary<string, string?> Configuration { get; }
}
