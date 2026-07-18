using Grpc.Core;
using Ino.Core;
using Ino.Core.Hosting.Brain;
using Ino.Gateway;
using Ino.Gateway.Grpc.Services;
using Ino.Grpc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using AuthProperty = Grpc.Core.AuthProperty;

namespace Ino.E2E.Tests;

// Strategy B — direct handler invocation with a stubbed IInoGateway.
// Verifies that InoGrpcService.AskIno correctly maps the request fields
// through IInoGateway.AskAsync and shapes the InoResponse into the wire
// AskInoResponse. No Aspire AppHost required; the test is fast and
// deterministic.
public class AskInoTests
{
    [Fact]
    public async Task AskIno_routes_plan_trip_to_PlanTripPlan()
    {
        const string correlationId = "corr-bali-42";
        const string replyText = "Here is your trip plan to Bali!";

        var gateway = Substitute.For<IInoGateway>();
        gateway
            .AskAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new InoResponse(
                Text: replyText,
                CorrelationId: correlationId,
                Rfw: null,
                Success: true,
                Source: "Regex"));

        var service = new InoGrpcService(gateway, new BrainPulseHub(), NullLogger<InoGrpcService>.Instance);
        var request = new AskInoRequest
        {
            Prompt = "plan a trip to Bali next month",
            UserId = "test-user",
            SessionId = "default",
        };

        var response = await service.AskIno(request, new TestServerCallContext());

        Assert.True(response.Success);
        Assert.False(string.IsNullOrEmpty(response.Reply), "Reply must be non-empty");
        Assert.False(string.IsNullOrEmpty(response.CorrelationId), "CorrelationId must be non-empty");
        Assert.Equal(replyText, response.Reply);
        Assert.Equal(correlationId, response.CorrelationId);
        Assert.Equal("Regex", response.Source);

        await gateway.Received(1).AskAsync(
            "plan a trip to Bali next month",
            "test-user",
            "default",
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskIno_defaults_anonymous_user_when_user_id_is_blank()
    {
        var gateway = Substitute.For<IInoGateway>();
        gateway
            .AskAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new InoResponse("ok", "corr-1", null, true, null));

        var service = new InoGrpcService(gateway, new BrainPulseHub(), NullLogger<InoGrpcService>.Instance);
        await service.AskIno(new AskInoRequest { Prompt = "hello" }, new TestServerCallContext());

        await gateway.Received(1).AskAsync(
            Arg.Any<string>(), "anonymous", Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskIno_null_source_becomes_empty_string_on_wire()
    {
        var gateway = Substitute.For<IInoGateway>();
        gateway
            .AskAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new InoResponse("ok", "corr-1", null, true, Source: null));

        var service = new InoGrpcService(gateway, new BrainPulseHub(), NullLogger<InoGrpcService>.Instance);
        var response = await service.AskIno(
            new AskInoRequest { Prompt = "hi", UserId = "u1", SessionId = "s1" },
            new TestServerCallContext());

        // proto3 strings must never be null on the wire — empty string is the
        // correct default when the source is unknown.
        Assert.Equal(string.Empty, response.Source);
    }

    [Fact]
    public async Task AskIno_populates_rfw_fields_when_payload_present()
    {
        var descBytes = "import { core };\nwidget root..."u8.ToArray();
        var dataBytes = "{\"dest\":\"Bali\"}"u8.ToArray();
        var rfw = new RfwPayload("ino.travel.trip", descBytes, dataBytes);

        var gateway = Substitute.For<IInoGateway>();
        gateway
            .AskAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new InoResponse("Done", "corr-rfw", rfw, true, "Llm"));

        var service = new InoGrpcService(gateway, new BrainPulseHub(), NullLogger<InoGrpcService>.Instance);
        var response = await service.AskIno(
            new AskInoRequest { Prompt = "show me my trip", UserId = "u1", SessionId = "s1" },
            new TestServerCallContext());

        Assert.True(response.Success);
        Assert.Equal("rfw/ino.travel.trip", response.ContentType);
        Assert.False(response.RfwDescription.IsEmpty);
        Assert.False(response.RfwData.IsEmpty);
    }

    // Minimal ServerCallContext so direct handler tests don't need a real
    // gRPC server. Only CancellationToken is wired; all other members throw
    // NotImplementedException if called (they never are in AskIno).
    sealed class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "AskIno";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore =>
            new("test", new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
            => Task.CompletedTask;
    }
}
