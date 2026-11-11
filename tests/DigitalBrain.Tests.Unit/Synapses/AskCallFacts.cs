using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Synapses;

public sealed class FakeProbe : IProbe
{
    public string? Id { get; private set; }
    public JsonElement Body { get; private set; }
    public CancellationToken Token { get; private set; }

    /// <summary>When set, Get returns this instead of {"id": id}.</summary>
    public Func<string, CancellationToken, Task<JsonElement>>? OnGet { get; set; }

    public Task<JsonElement> Get(string id, CancellationToken ct)
    {
        (Id, Token) = (id, ct);
        return OnGet?.Invoke(id, ct) ?? Task.FromResult(JsonSerializer.SerializeToElement(new { id }));
    }

    public Task<string> Put(string id, JsonElement body, CancellationToken ct)
    {
        (Id, Body, Token) = (id, body.Clone(), ct);
        return Task.FromResult("stored " + id);
    }
}

public sealed class AskCallFacts
{
    private interface IVoidAndOverloadProbe
    {
        Task Touch(string id, CancellationToken ct);
        Task<int> Same(int a);
        Task<int> Same(int a, int b);
    }

    private sealed class VoidAndOverloadProbe : IVoidAndOverloadProbe
    {
        public string? Touched { get; private set; }

        public Task Touch(string id, CancellationToken ct)
        {
            Touched = id;
            return Task.CompletedTask;
        }

        public Task<int> Same(int a) => Task.FromResult(a);
        public Task<int> Same(int a, int b) => Task.FromResult(a + b);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArgumentsBindByNameInAnyOrder()
    {
        var probe = new FakeProbe();
        var result = await AskCall.Invoke(typeof(IProbe), probe, "Put", JsonElement.Parse("""{"body":{"a":1},"id":"7"}"""), Ct);

        Assert.Equal("\"stored 7\"", result.GetRawText());
        Assert.Equal(("7", """{"a":1}"""), (probe.Id, probe.Body.GetRawText()));
    }

    [Theory]
    [InlineData("Run", """{"id":"7"}""")]                      // an operation of another contract
    [InlineData("Get", """{}""")]                              // a missing argument
    [InlineData("Get", """{"id":"7","extra":1}""")]            // an extra argument
    [InlineData("Get", """{"ID":"7"}""")]                      // names are matched exactly
    [InlineData("Get", """{"id":{"not":"a string"}}""")]       // the wrong shape
    public async Task WhatDoesNotFitTheContractIsABindingException(string operation, string arguments) =>
        await Assert.ThrowsAsync<BindingException>(() => AskCall.Invoke(typeof(IProbe), new FakeProbe(), operation, JsonElement.Parse(arguments), Ct));

    [Fact]
    public async Task TheTokenReachesTheCapability()
    {
        var probe = new FakeProbe();
        using var cts = new CancellationTokenSource();
        _ = await AskCall.Invoke(typeof(IProbe), probe, "Get", JsonElement.Parse("""{"id":"7"}"""), cts.Token);
        Assert.Equal(cts.Token, probe.Token);
    }

    [Fact]
    public async Task AMethodWithNoResultReturnsJsonNull()
    {
        var probe = new VoidAndOverloadProbe();
        var result = await AskCall.Invoke(typeof(IVoidAndOverloadProbe), probe, "Touch", JsonElement.Parse("""{"id":"9"}"""), Ct);

        Assert.Equal("9", probe.Touched);
        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    [Fact]
    public async Task WrongCapabilityAmbiguousMethodOrNonObjectArgumentsIsABindingException()
    {
        var probe = new VoidAndOverloadProbe();
        await Assert.ThrowsAsync<BindingException>(() => AskCall.Invoke(typeof(IProbe), new object(), "Get", JsonElement.Parse("""{"id":"7"}"""), Ct));
        await Assert.ThrowsAsync<BindingException>(() => AskCall.Invoke(typeof(IVoidAndOverloadProbe), probe, "Same", JsonElement.Parse("""{"a":1}"""), Ct));
        await Assert.ThrowsAsync<BindingException>(() => AskCall.Invoke(typeof(IProbe), new FakeProbe(), "Get", JsonElement.Parse("""["7"]"""), Ct));
        await Assert.ThrowsAsync<BindingException>(() => AskCall.Invoke(typeof(IProbe), new FakeProbe(), "Get", JsonElement.Parse("""{"id":"1","id":"2"}"""), Ct));
    }
}
