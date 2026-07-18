using Xunit;

namespace Ino.Core.Tests;

/// <summary>
/// Locks the structured <see cref="RfwPayload"/> upgrade to <see cref="NeuronResult.Rfw"/>:
/// (1) <c>WithRfwPayload</c> attaches and reads back, (2) the with-expression replaces an
/// existing payload without leaking the old reference, (3) byte-array fields are concrete
/// <c>byte[]</c> (not <c>IReadOnlyList&lt;byte&gt;</c>) so cross-silo deep-copy doesn't
/// trip the <c>&lt;&gt;z__ReadOnlyArray&lt;T&gt;</c> codec trap.
/// </summary>
public sealed class NeuronResultRfwTests
{
    [Fact]
    public void Ok_with_RfwPayload_roundtrips()
    {
        var payload = new RfwPayload("ino.test", new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });
        var result = NeuronResult.Ok("hello").WithRfwPayload(payload);

        Assert.True(result.Success);
        Assert.Equal("hello", result.Message);
        Assert.Equal(payload, result.Rfw);
        Assert.Equal("ino.test", result.Rfw!.LibraryName);
    }

    [Fact]
    public void WithRfwPayload_replaces_existing_payload()
    {
        var first = new RfwPayload("a", new byte[] { 1 }, new byte[] { 2 });
        var second = new RfwPayload("b", new byte[] { 3 }, new byte[] { 4 });

        var result = NeuronResult.Ok().WithRfwPayload(first).WithRfwPayload(second);

        Assert.Equal(second, result.Rfw);
        Assert.NotEqual(first, result.Rfw);
    }

    [Fact]
    public void RfwPayload_byte_arrays_are_concrete_T_array()
    {
        // Compile-time enforcement of the <>z__ReadOnlyArray<T> codec trap:
        // if a future refactor changes RfwPayload to IReadOnlyList<byte>,
        // Orleans cross-silo deep-copy throws CodecNotFoundException at runtime.
        var p = new RfwPayload("x", System.Array.Empty<byte>(), System.Array.Empty<byte>());

        Assert.IsType<byte[]>(p.DescriptionDsl);
        Assert.IsType<byte[]>(p.DataPayload);
    }
}
