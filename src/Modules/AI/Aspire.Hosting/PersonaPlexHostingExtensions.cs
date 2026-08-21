using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI.PersonaPlex;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

public sealed class PersonaPlexHostOptions
{
    public bool Enabled { get; set; }

    public string ModelDirectory { get; set; } = string.Empty;

    public int CudaDeviceId { get; set; }

    public int MaxSessions { get; set; } = 1;
}

public static class PersonaPlexHostingExtensions
{
    private static readonly string ConfigurationPrefix =
        $"{PersonaPlexOptions.SectionName.Replace(":", "__", StringComparison.Ordinal)}__";

    public static DigitalBrainModuleBuilder<AIModule> WithPersonaPlex(
        this DigitalBrainModuleBuilder<AIModule> module,
        Action<PersonaPlexHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configure);

        var personaPlex = module.Brain.GetOrAddState(
            static brain => new PersonaPlexHostingState(brain),
            out var added);
        if (added)
        {
            module.AddProjection(personaPlex);
        }

        personaPlex.Configure(configure);
        return module;
    }

    private sealed class PersonaPlexHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private readonly PersonaPlexHostOptions _options = new();
        private bool _configured;

        internal void Configure(Action<PersonaPlexHostOptions> configure)
        {
            if (_configured)
            {
                throw new InvalidOperationException(
                    $"PersonaPlex is already configured on brain '{brain.Name}'. Call WithPersonaPlex once.");
            }

            configure(_options);
            if (_options.CudaDeviceId < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_options.CudaDeviceId),
                    "PersonaPlex requires a non-negative CUDA device ID.");
            }

            if (_options.MaxSessions <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_options.MaxSessions),
                    "PersonaPlex requires at least one session.");
            }

            _configured = true;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder
                .WithEnvironment($"{ConfigurationPrefix}Enabled", _options.Enabled.ToString())
                .WithEnvironment($"{ConfigurationPrefix}ModelDirectory", _options.ModelDirectory)
                .WithEnvironment($"{ConfigurationPrefix}CudaDeviceId", _options.CudaDeviceId.ToString())
                .WithEnvironment($"{ConfigurationPrefix}MaxSessions", _options.MaxSessions.ToString());
        }
    }
}
