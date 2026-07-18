using FluentAssertions;
using Ino.Core;
using Ino.NeuronTesting;
using Ino.NeuronTesting.Attributes;
using Ino.NeuronTesting.Internals;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public sealed class NeuronIdResolverTests
{
    [Fact]
    public void Resolves_from_NeuronIdAttribute_when_present()
    {
        var id = NeuronIdResolver.Resolve(typeof(WithAttribute));
        id.Value.Should().Be("test.with-attribute");
    }

    [Fact]
    public void Throws_when_no_attribute_and_no_domain_match()
    {
        Action act = () => NeuronIdResolver.Resolve(typeof(WithoutAnything));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no NeuronId*");
    }

    [NeuronId("test.with-attribute")]
    sealed class WithAttribute { }

    sealed class WithoutAnything { }
}
