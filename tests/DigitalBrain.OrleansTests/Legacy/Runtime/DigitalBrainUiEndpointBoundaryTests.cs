extern alias McpProject;

using System.Collections;
using System.Reflection;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using DigitalBrainUiEndpoints = McpProject::DigitalBrain.Mcp.DigitalBrainUiEndpoints;
using GrpcFeatureSourceFile = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceFile;
using GrpcFeatureSourceSnapshot = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceSnapshot;
using GetFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetFeatureDraftRequest;
using ReviseFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureDraftRequest;
using ReviseFeatureSourceInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureSourceInput;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class DigitalBrainUiEndpointBoundaryTests
{
    [Fact]
    public async Task Request_mapping_preserves_control_flow_and_fails_closed_for_unexpected_canaries()
    {
        var logger = new CapturingLogger<DigitalBrainUiEndpoints>();
        var endpoints = new DigitalBrainUiEndpoints(null!, null!, logger);
        var request = new GetFeatureDraftRequest { DraftId = "draft-boundary" };
        var existing = new RpcException(new Status(StatusCode.DeadlineExceeded, "existing-safe-detail"));

        var authorityArgument = await Assert.ThrowsAsync<RpcException>(() => endpoints.GetFeatureDraftAsync(
            Context(new ThrowingSet(new ArgumentException("mapping-argument-canary"))),
            request,
            CancellationToken.None));
        var authorityUnexpected = await Assert.ThrowsAsync<RpcException>(() => endpoints.GetFeatureDraftAsync(
            Context(new ThrowingSet(new InvalidOperationException("mapping-unexpected-canary"))),
            request,
            CancellationToken.None));
        var malformed = await Assert.ThrowsAsync<RpcException>(() => endpoints.GetFeatureDraftAsync(
            Context(new HashSet<string>(["feature.manage"], StringComparer.Ordinal)),
            new GetFeatureDraftRequest(),
            CancellationToken.None));
        var mappingUnexpected = Assert.Throws<RpcException>(() => MapRequest<object>(
            endpoints,
            Context(new HashSet<string>(["feature.manage"], StringComparer.Ordinal)),
            () => throw new InvalidOperationException("mapper-framework-canary")));
        var preserved = await Assert.ThrowsAsync<RpcException>(() => endpoints.GetFeatureDraftAsync(
            Context(new ThrowingSet(existing)),
            request,
            CancellationToken.None));
        await Assert.ThrowsAsync<OperationCanceledException>(() => endpoints.GetFeatureDraftAsync(
            Context(new ThrowingSet(new OperationCanceledException("mapping-cancellation-canary"))),
            request,
            CancellationToken.None));

        Assert.Equal(StatusCode.Internal, authorityArgument.StatusCode);
        Assert.Equal(StatusCode.Internal, authorityUnexpected.StatusCode);
        Assert.Equal("The Feature request could not be completed.", authorityArgument.Status.Detail);
        Assert.Equal("The Feature request could not be completed.", authorityUnexpected.Status.Detail);
        Assert.Equal(StatusCode.InvalidArgument, malformed.StatusCode);
        Assert.Equal("The Feature request is invalid.", malformed.Status.Detail);
        Assert.Equal(StatusCode.Internal, mappingUnexpected.StatusCode);
        Assert.Equal("The Feature request could not be completed.", mappingUnexpected.Status.Detail);
        Assert.Same(existing, preserved);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("canary", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("src/COM¹/Feature.csproj")]
    [InlineData("src/com¹.txt/Feature.csproj")]
    [InlineData("src/COM²/Feature.csproj")]
    [InlineData("src/cOm².json/Feature.csproj")]
    [InlineData("src/COM³/Feature.csproj")]
    [InlineData("src/Com³.cs/Feature.csproj")]
    [InlineData("src/LPT¹/Feature.csproj")]
    [InlineData("src/lpt¹.txt/Feature.csproj")]
    [InlineData("src/LPT²/Feature.csproj")]
    [InlineData("src/lPt².json/Feature.csproj")]
    [InlineData("src/LPT³/Feature.csproj")]
    [InlineData("src/Lpt³.cs/Feature.csproj")]
    public async Task Public_revision_mapping_rejects_Windows_reserved_device_aliases(string invalidPath)
    {
        var logger = new CapturingLogger<DigitalBrainUiEndpoints>();
        var endpoints = new DigitalBrainUiEndpoints(null!, null!, logger);
        var source = new GrpcFeatureSourceSnapshot
        {
            ImplementationProjectPath = invalidPath,
            ScenarioProjectPath = "tests/Feature.Scenarios/Feature.Scenarios.csproj"
        };
        source.Files.Add([
            new GrpcFeatureSourceFile { Path = invalidPath, Content = "implementation" },
            new GrpcFeatureSourceFile
            {
                Path = source.ScenarioProjectPath,
                Content = "scenarios"
            }
        ]);
        var request = new ReviseFeatureDraftRequest
        {
            DraftId = "draft-reserved-device",
            ExpectedRevision = 0,
            IdempotencyId = "source-reserved-device",
            ReviseSource = new ReviseFeatureSourceInput { Source = source }
        };

        var rejected = await Assert.ThrowsAsync<RpcException>(() => endpoints.ReviseFeatureDraftAsync(
            Context(new HashSet<string>(["feature.manage"], StringComparer.Ordinal)),
            request,
            CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, rejected.StatusCode);
        Assert.Equal("The Feature request is invalid.", rejected.Status.Detail);
    }

    [Fact]
    public async Task Invocation_boundary_maps_only_typed_application_failures_and_preserves_control_flow()
    {
        var logger = new CapturingLogger<DigitalBrainUiEndpoints>();
        var endpoints = new DigitalBrainUiEndpoints(null!, null!, logger);
        var deadline = new RpcException(new Status(StatusCode.DeadlineExceeded, "existing-deadline"));
        var argument = await Assert.ThrowsAsync<RpcException>(() => InvokeAsync<object>(
            endpoints,
            () => Task.FromException<object>(new ArgumentException("dependency-argument-canary"))));
        var unauthorized = await Assert.ThrowsAsync<RpcException>(() => InvokeAsync<object>(
            endpoints,
            () => Task.FromException<object>(new UnauthorizedAccessException("dependency-authority-canary"))));
        var limit = await Assert.ThrowsAsync<RpcException>(() => InvokeAsync<object>(
            endpoints,
            () => Task.FromException<object>(new FeatureCommandRejectedException(FeatureCommandRejectionReason.Limit))));
        var unavailable = await Assert.ThrowsAsync<RpcException>(() => InvokeAsync<object>(
            endpoints,
            () => Task.FromException<object>(new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable))));
        var preserved = await Assert.ThrowsAsync<RpcException>(() => InvokeAsync<object>(
            endpoints,
            () => Task.FromException<object>(deadline)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeAsync<object>(
            endpoints,
            () => Task.FromException<object>(new OperationCanceledException("dependency-cancellation-canary"))));

        Assert.Equal(StatusCode.Internal, argument.StatusCode);
        Assert.Equal(StatusCode.Internal, unauthorized.StatusCode);
        Assert.Equal("The Feature request could not be completed.", argument.Status.Detail);
        Assert.Equal("The Feature request could not be completed.", unauthorized.Status.Detail);
        Assert.Equal(StatusCode.ResourceExhausted, limit.StatusCode);
        Assert.Equal("The Feature request exceeds a configured limit.", limit.Status.Detail);
        Assert.Equal(StatusCode.Unavailable, unavailable.StatusCode);
        Assert.Equal("The Feature service is temporarily unavailable. Retry the same request.", unavailable.Status.Detail);
        Assert.Same(deadline, preserved);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("canary", StringComparison.Ordinal));
    }

    [Fact]
    public void Projection_boundary_returns_only_fixed_Internal_and_preserves_existing_RpcException()
    {
        var logger = new CapturingLogger<DigitalBrainUiEndpoints>();
        var endpoints = new DigitalBrainUiEndpoints(null!, null!, logger);
        var existing = new RpcException(new Status(StatusCode.Cancelled, "existing-cancelled"));

        var corrupt = Assert.Throws<RpcException>(() => Project<object>(
            endpoints,
            () => throw new InvalidDataException("projection-output-canary")));
        var preserved = Assert.Throws<RpcException>(() => Project<object>(
            endpoints,
            () => throw existing));

        Assert.Equal(StatusCode.Internal, corrupt.StatusCode);
        Assert.Equal("The Feature request could not be completed.", corrupt.Status.Detail);
        Assert.Same(existing, preserved);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("canary", StringComparison.Ordinal));
    }

    private static RuntimeRequestContext Context(IReadOnlySet<string> grants) => new(
        new BrainOwnerId("owner-boundary"),
        new ActorId("actor-boundary"),
        new SessionId("session-boundary"),
        AuthAssurance.Oidc,
        "correlation-boundary",
        null,
        grants,
        "conversation-boundary");

    private static Task<T> InvokeAsync<T>(DigitalBrainUiEndpoints endpoints, Func<Task<T>> invocation)
    {
        var method = typeof(DigitalBrainUiEndpoints)
            .GetMethod("InvokeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(T));
        return (Task<T>)method.Invoke(endpoints, [invocation])!;
    }

    private static T MapRequest<T>(
        DigitalBrainUiEndpoints endpoints,
        RuntimeRequestContext context,
        Func<T> mapping)
    {
        var method = typeof(DigitalBrainUiEndpoints)
            .GetMethod("MapRequest", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(T));
        try
        {
            return (T)method.Invoke(endpoints, [context, mapping])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is RpcException rpcException)
        {
            throw rpcException;
        }
    }

    private static T Project<T>(DigitalBrainUiEndpoints endpoints, Func<T> projection)
    {
        var method = typeof(DigitalBrainUiEndpoints)
            .GetMethod("Project", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(T));
        try
        {
            return (T)method.Invoke(endpoints, [projection])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is RpcException rpcException)
        {
            throw rpcException;
        }
    }

    private sealed class ThrowingSet(Exception exception) : IReadOnlySet<string>
    {
        public int Count => 1;
        public bool Contains(string item) => false;
        public bool IsProperSubsetOf(IEnumerable<string> other) => false;
        public bool IsProperSupersetOf(IEnumerable<string> other) => false;
        public bool IsSubsetOf(IEnumerable<string> other) => false;
        public bool IsSupersetOf(IEnumerable<string> other) => false;
        public bool Overlaps(IEnumerable<string> other) => false;
        public bool SetEquals(IEnumerable<string> other) => false;
        public IEnumerator<string> GetEnumerator() => throw exception;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
