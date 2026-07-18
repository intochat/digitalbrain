using Xunit;

namespace Ino.Core.Tests;

public sealed class NeuronResultTests
{
    // Minimal ISynapse for testing the With<T> helper
    private sealed record DummyResponse(string Value) : ISynapse;

    // Second ISynapse type for wrong-type TryGetPayload coverage.
    private sealed record OtherPayload(int N) : ISynapse;

    [Fact]
    public void Ok_WithNoMessage_ReturnsSuccess()
    {
        var result = NeuronResult.Ok();

        Assert.True(result.Success);
        Assert.Null(result.Message);
        Assert.Null(result.Error);
        Assert.Null(result.ResponsePayload);
        Assert.Null(result.Rfw);
    }

    [Fact]
    public void Ok_WithMessage_CarriesMessage()
    {
        var result = NeuronResult.Ok("done");

        Assert.True(result.Success);
        Assert.Equal("done", result.Message);
    }

    [Fact]
    public void Fail_WithError_ReturnsFailureCarryingError()
    {
        var error = new SynapseError(SynapseErrorCode.NoCanonicalHandler, "something broke");
        var result = NeuronResult.Fail(error);

        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
        Assert.Equal("something broke", result.Message);
    }

    [Fact]
    public void Fail_WithCodeAndMessage_ConstructsSynapseError()
    {
        var result = NeuronResult.Fail(SynapseErrorCode.NoCanonicalHandler, "missing");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(SynapseErrorCode.NoCanonicalHandler, result.Error!.Code);
        Assert.Equal("missing", result.Error.Message);
    }

    [Fact]
    public void With_AttachesResponsePayload()
    {
        var payload = new DummyResponse("hello");
        var result = NeuronResult.Ok().With(payload);

        Assert.Equal(payload, result.ResponsePayload);
    }

    [Fact]
    public void TryGetPayload_WithMatchingType_ReturnsTrue()
    {
        var payload = new DummyResponse("hello");
        var result = NeuronResult.Ok().With(payload);

        Assert.True(result.TryGetPayload<DummyResponse>(out var extracted));
        Assert.Equal(payload, extracted);
    }

    [Fact]
    public void TryGetPayload_WithNoPayload_ReturnsFalse()
    {
        var result = NeuronResult.Ok();

        Assert.False(result.TryGetPayload<DummyResponse>(out _));
    }

    [Fact]
    public void WithRfwPayload_AttachesRfwPayload()
    {
        var payload = new RfwPayload("ino.test", new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });
        var result = NeuronResult.Ok().WithRfwPayload(payload);

        Assert.Equal(payload, result.Rfw);
    }

    [Fact]
    public void TryGetPayload_returns_false_and_null_when_payload_missing()
    {
        var result = NeuronResult.Ok();
        var success = result.TryGetPayload<DummyResponse>(out var payload);

        Assert.False(success);
        Assert.Null(payload);
    }

    [Fact]
    public void TryGetPayload_returns_false_and_null_when_payload_wrong_type()
    {
        var result = NeuronResult.Ok().With(new DummyResponse("x"));
        var success = result.TryGetPayload<OtherPayload>(out var payload);

        Assert.False(success);
        Assert.Null(payload);
    }
}
