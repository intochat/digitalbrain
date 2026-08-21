using DigitalBrain.AI.PersonaPlex;
using Xunit;

namespace DigitalBrain.AI.PersonaPlex.Tests;

public sealed class PersonaPlexAudioFrameTests
{
    [Fact]
    public void CreateAcceptsExactly1920Samples()
    {
        var frame = PersonaPlexAudioFrame.Create(1, new short[1920]);

        Assert.Equal(1, frame.Sequence);
    }

    [Fact]
    public void CreateRejectsFramesWithAnyOtherSampleCount()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PersonaPlexAudioFrame.Create(1, new short[1919]));

        Assert.Equal("pcm16", exception.ParamName);
    }
}
