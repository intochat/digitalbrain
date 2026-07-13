using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Tests.TestSupport;

// Minimal ServerCallContext for unit-testing gRPC service methods directly (no transport).
public sealed class TestServerCallContext : ServerCallContext
{
    private readonly CancellationToken _cancellationToken;
    private readonly Metadata _requestHeaders;
    private readonly IDictionary<object, object> _userState = new Dictionary<object, object>
    {
        ["__HttpContext"] = new DefaultHttpContext()
    };

    private TestServerCallContext(CancellationToken cancellationToken = default, Metadata? requestHeaders = null)
    {
        _cancellationToken = cancellationToken;
        _requestHeaders = requestHeaders ?? [];
    }

    public static TestServerCallContext Create(CancellationToken cancellationToken = default) => new(cancellationToken);

    public static TestServerCallContext WithHeaders(params (string Key, string Value)[] headers)
        => WithHeaders(default, headers);

    public static TestServerCallContext WithHeaders(CancellationToken cancellationToken, params (string Key, string Value)[] headers)
    {
        var metadata = new Metadata();
        foreach (var (key, value) in headers)
        {
            metadata.Add(key, value);
        }

        return new TestServerCallContext(cancellationToken, metadata);
    }

    protected override string MethodCore => "test";
    protected override string HostCore => "test";
    protected override string PeerCore => "test";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => _requestHeaders;
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    protected override Metadata ResponseTrailersCore { get; } = [];
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(string.Empty, []);
    protected override IDictionary<object, object> UserStateCore => _userState;
    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
