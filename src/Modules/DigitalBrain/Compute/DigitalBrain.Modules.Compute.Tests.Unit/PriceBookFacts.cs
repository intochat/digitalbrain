using DigitalBrain.Compute;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class PriceBookFacts
{
    [Fact]
    public void PricesConcreteModelsPerMillionAndLeavesLocalAndUnknownAtZero()
    {
        var book = new PriceBook();
        Assert.Equal("v0", book.Version);
        Assert.Equal(250m, book.PriceInCompute(PriceBook.ChatMeterId("gpt-5.6-luna", "input"), 1_000_000m));
        Assert.Equal(125m, book.PriceInCompute(PriceBook.ChatMeterId("gpt-5.6-luna", "cached"), 1_000_000m));
        Assert.Equal(0m, book.PriceInCompute(PriceBook.ChatMeterId("gemma4:e2b", "input"), 1_000_000m));
        Assert.True(book.IsLocal("gemma4:e2b"));
        Assert.False(book.IsLocal("gpt-5.6-luna"));
        Assert.Equal(0m, book.PriceInCompute("chat:unknown-model:input", 1_000_000m));
        Assert.Equal(0m, book.PriceInCompute(PriceBook.EmbeddingMeterId("embeddinggemma"), 1_000_000m));
    }
}
