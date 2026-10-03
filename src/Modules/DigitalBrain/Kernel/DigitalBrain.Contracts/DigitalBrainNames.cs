namespace DigitalBrain.Contracts;

public static class DigitalBrainNames
{
    public const string GrainStateContainer = "digitalbrain-v2-state";

    public const string Storage = "storage";
    public const string Clustering = "clustering";
    public const string Reminders = "reminders";
    public const string GrainState = "grainstate";
    public const string DefaultGrainStorage = "Default";
    public const string OrleansDashboardPath = "/orleans";
    public const string MasterKeyConfigurationKey = "DigitalBrain:MasterKey";
    public const string MasterKeyEnvironmentVariable = "DigitalBrain__MasterKey";
    // An optional JSON file of additional configuration the runtime loads last (the secrets-file
    // pattern): deployment credentials, test seeds — any setting that must not travel as plain env.
    public const string ConfigurationFileKey = "DigitalBrain:ConfigurationFile";
    public const string ConfigurationFileEnvironmentVariable = "DigitalBrain__ConfigurationFile";
}
