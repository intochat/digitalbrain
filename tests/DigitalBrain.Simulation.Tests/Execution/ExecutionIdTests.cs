using DigitalBrain.Abstractions.Execution;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class ExecutionIdTests
{
    [Fact]
    public void New_produces_non_empty_id_and_round_trips_string_key()
    {
        var id = ExecutionId.New();
        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id, ExecutionId.Parse(id.ToString()));
    }

    [Fact]
    public void Empty_guid_is_rejected()
        => Assert.Throws<ArgumentException>(() => new ExecutionId(Guid.Empty));

    [Fact]
    public void ContextPath_trims_slashes_and_rejects_blank()
    {
        Assert.Equal("gmail.search", new ContextPath(" /gmail.search/ ").Value);
        Assert.Throws<ArgumentException>(() => new ContextPath(" "));
        Assert.Throws<ArgumentException>(() => new ContextPath(null!));
    }

    [Fact]
    public void ContextDigest_rejects_blank_sha()
    {
        Assert.Equal("abc", new ContextDigest("abc").Sha256Hex);
        Assert.Throws<ArgumentException>(() => new ContextDigest(" "));
        Assert.Throws<ArgumentException>(() => new ContextDigest(null!));
    }
}
