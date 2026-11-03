using System.Net;
using DigitalBrain.Capabilities.Compute;

namespace DigitalBrain.Capabilities.Compute.Tests.Unit;

public sealed class ExternalCallHandlerFacts
{
    [Fact]
    public async Task HandlerBeginsAndCompletesAnExternalCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new ModulePriceListRegistry(
        [
            new ModulePriceListRegistration("gmail", typeof(DummyPriceList),
            [
                new ExternalOperationDescriptor("gmail.messages.list", "google", "request"),
            ]),
        ]);
        var meter = new FakeCostMeter();
        var accessor = new FakeContextAccessor("trace-1");

        using var okTransport = new StubTransport(HttpStatusCode.OK);
        using var okHandler = new ExternalCallHandler(meter, registry, accessor) { InnerHandler = okTransport };
        using var okClient = new HttpClient(okHandler);

        using var okRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/messages");
        okRequest.SetExternalCall(new ExternalCallMetadata("gmail.messages.list", Funding.OwnConnection, new CostSubject(App: "mail"), "inv-1"));

        using var okResponse = await okClient.SendAsync(okRequest, ct);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        Assert.Single(meter.Begun);
        Assert.Equal("gmail.messages.list", meter.Begun[0].Operation);
        Assert.Equal("inv-1", meter.Begun[0].InvocationId);
        Assert.Single(meter.Dispatched);
        var okCompleted = Assert.Single(meter.Completed);
        Assert.Equal(ExternalCallOutcomeKind.Succeeded, okCompleted.Outcome.Kind);
        Assert.Equal(200, okCompleted.Outcome.HttpStatusCode);
        Assert.NotNull(okResponse.GetExternalAccounting());
        Assert.Equal(ExternalAccountingStatus.Settled, okResponse.GetExternalAccounting()!.Status);

        meter.Reset();

        using var failTransport = new StubTransport(HttpStatusCode.InternalServerError);
        using var failHandler = new ExternalCallHandler(meter, registry, accessor) { InnerHandler = failTransport };
        using var failClient = new HttpClient(failHandler);

        using var failRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/messages");
        failRequest.SetExternalCall(new ExternalCallMetadata("gmail.messages.list", Funding.OwnConnection, null, "inv-2"));

        using var failResponse = await failClient.SendAsync(failRequest, ct);
        Assert.Equal(HttpStatusCode.InternalServerError, failResponse.StatusCode);
        Assert.Single(meter.Begun);
        Assert.Single(meter.Dispatched);
        var failCompleted = Assert.Single(meter.Completed);
        Assert.Equal(ExternalCallOutcomeKind.HttpFailed, failCompleted.Outcome.Kind);
        Assert.Equal(500, failCompleted.Outcome.HttpStatusCode);
    }

    private sealed class DummyPriceList : IPriceList
    {
        public long? MicrosPerUnit(CostKind kind, string item) => null;
    }

    private sealed class FakeContextAccessor(string traceId) : IExternalCallContextAccessor
    {
        public string RequireTraceId() => traceId;
    }

    private sealed class StubTransport(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
    }

    private sealed class FakeCostMeter : ICostMeter
    {
        private int _seq;
        public List<ExternalCallRequest> Begun { get; } = [];
        public List<ExternalCallHandle> Dispatched { get; } = [];
        public List<(ExternalCallHandle Handle, ExternalCallOutcome Outcome)> Completed { get; } = [];

        public void Reset()
        {
            Begun.Clear();
            Dispatched.Clear();
            Completed.Clear();
        }

        public ValueTask<Cost> RecordAsync(CostKind kind, string item, decimal quantity, string unit, Funding funding,
            string costId, CancellationToken cancellationToken, CostSubject? subject = null)
            => throw new NotSupportedException();

        public ValueTask<ExternalCallHandle> BeginExternalCallAsync(ExternalCallRequest request, CancellationToken cancellationToken)
        {
            Begun.Add(request);
            return ValueTask.FromResult(new ExternalCallHandle($"handle-{++_seq}"));
        }

        public ValueTask MarkExternalCallDispatchStartedAsync(ExternalCallHandle handle, CancellationToken cancellationToken)
        {
            Dispatched.Add(handle);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ExternalCallCompletion> CompleteExternalCallAsync(
            ExternalCallHandle handle, ExternalCallOutcome outcome, CancellationToken cancellationToken)
        {
            Completed.Add((handle, outcome));
            return ValueTask.FromResult(new ExternalCallCompletion(handle, ExternalAccountingStatus.Settled));
        }
    }
}
