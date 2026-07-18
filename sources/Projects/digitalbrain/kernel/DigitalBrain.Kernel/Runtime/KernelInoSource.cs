using System.Reflection;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime
{
    public sealed class KernelInoSource(IContractCatalog catalog, ILogger<KernelInoSource> logger) : IInterpretedNeuronSource
    {
        public Task<IReadOnlyList<InterpretedNeuronRegistration>> DiscoverAsync(CancellationToken cancellationToken)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var registrations = new List<InterpretedNeuronRegistration>();

            var files = new[]
            {
                "Runtime.Settings.settings.ino",
                "Ino.Ino.ino"
            };

            foreach (var file in files)
            {
                var resourceName = $"DigitalBrain.Kernel.{file}";
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    logger.LogWarning("KernelInoSource: Could not find embedded resource '{ResourceName}'", resourceName);
                    continue;
                }

                using var reader = new StreamReader(stream);
                var source = reader.ReadToEnd();

                var compiled = InoCompiler.Compile(source, catalog);
                if (!compiled.Success || compiled.Linked is null)
                {
                    logger.LogError("KernelInoSource: Failed to compile '{ResourceName}': {Errors}",
                        resourceName, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
                    continue;
                }

                var registration = LinkedPortCatalogContributor.BuildRegistration(source, compiled.Linked);
                registrations.Add(registration);
                logger.LogInformation("KernelInoSource: Registered interpreted neuron '{Fqn}' from embedded resource '{ResourceName}'",
                    compiled.Linked.Doc.Fqn, resourceName);
            }

            return Task.FromResult<IReadOnlyList<InterpretedNeuronRegistration>>(registrations);
        }
    }
}
