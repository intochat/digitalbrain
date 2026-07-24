using DigitalBrain.Generated;
using Xunit;

namespace DigitalBrain.ModuleTests
{
    public sealed class GeneratedVocabularyConsumerContracts
    {
        [Fact]
        public void FullyQualifiedAndUniqueShortNeuronNamesResolve()
        {
            Assert.True(GeneratedTestVocabulary.TryResolveNeuron(
                typeof(IProbeTarget).FullName!,
                out var qualified));
            Assert.True(GeneratedTestVocabulary.TryResolveNeuron(
                nameof(IProbeTarget),
                out var shortened));

            Assert.Equal(typeof(IProbeTarget).FullName, qualified.Identity);
            Assert.Equal(qualified.Identity, shortened.Identity);
        }

        [Fact]
        public void CompiledSynapseFactoryConstructsThePublicContract()
        {
            var created = GeneratedTestVocabulary.TryCreateSynapse(
                typeof(ProbePing).FullName!,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(ProbePing.Value)] = "compiled",
                },
                out var synapse);

            Assert.True(created);
            Assert.Equal(new ProbePing("compiled"), synapse);
        }

        [Fact]
        public void AmbiguousShortSynapseNameNeverSelectsACandidate()
        {
            Assert.False(GeneratedTestVocabulary.TryResolveSynapse(
                "VocabularyCollision",
                out _));
            Assert.True(GeneratedTestVocabulary.TryResolveSynapse(
                typeof(AmbiguousAlpha.VocabularyCollision).FullName!,
                out var alpha));
            Assert.True(GeneratedTestVocabulary.TryResolveSynapse(
                typeof(AmbiguousBeta.VocabularyCollision).FullName!,
                out var beta));

            Assert.Equal(
                typeof(AmbiguousAlpha.VocabularyCollision).FullName,
                alpha.Identity);
            Assert.Equal(
                typeof(AmbiguousBeta.VocabularyCollision).FullName,
                beta.Identity);
        }
    }
}

namespace AmbiguousAlpha
{
    public sealed record VocabularyCollision :
        DigitalBrain.Abstractions.Synapse;
}

namespace AmbiguousBeta
{
    public sealed record VocabularyCollision :
        DigitalBrain.Abstractions.Synapse;
}
