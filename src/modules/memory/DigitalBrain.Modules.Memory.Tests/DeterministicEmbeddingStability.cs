using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class DeterministicEmbeddingStability
{
    private static readonly float[] NearExpected =
    [
        0f,
        0.526924491f,
        0f,
        0f,
        0.618404448f,
        0f,
        0f,
        0.583032191f,
    ];

    [Fact(DisplayName = "Embed is a pure function of token bytes, not a process-randomized string hash")]
    public void Embed_matches_frozen_fnv1a_vectors_for_top_k_scenario()
    {
        var near = DeterministicEmbeddingGenerator.Embed("red apple fruit");
        var mid = DeterministicEmbeddingGenerator.Embed("red apple");
        var far = DeterministicEmbeddingGenerator.Embed("blue ocean water");
        var query = DeterministicEmbeddingGenerator.Embed("red apple fruit");

        Assert.Equal(NearExpected, near);

        var nearScore = Cosine(query, near);
        var midScore = Cosine(query, mid);
        var farScore = Cosine(query, far);

        Assert.Equal(1.0, nearScore, precision: 4);
        Assert.Equal(0.8499, midScore, precision: 4);
        Assert.Equal(0.3683, farScore, precision: 4);
        Assert.True(midScore > farScore);
    }

    [Fact(DisplayName = "Embed is case-insensitive and identical for equal tokens")]
    public void Embed_is_case_insensitive()
    {
        Assert.Equal(
            DeterministicEmbeddingGenerator.Embed("red"),
            DeterministicEmbeddingGenerator.Embed("RED"));
    }

    private static float Cosine(float[] left, float[] right)
    {
        float dot = 0f;
        for (var i = 0; i < left.Length; i++)
            dot += left[i] * right[i];
        return dot;
    }
}
