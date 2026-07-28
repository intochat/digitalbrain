using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(Mode = ParallelMode.None)]

namespace DigitalBrain.HostTests;

public sealed class AppHostCleanupContracts
{
    private static readonly TimeSpan TestCleanupBudget =
        TimeSpan.FromMilliseconds(150);

    [Fact(DisplayName = "A hung AppHost disposal completes within the cleanup budget")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "RunningAppHost owns the supplied distributed application.")]
    public async Task AHungAppHostDisposalCompletesWithinTheCleanupBudget()
    {
        var applicationHost = new ControllableHost(hangDuringDispose: true);
        var running = await CreateRunningAppHost(applicationHost);
        var disposal = running.DisposeAsync().AsTask();
        var bound = Task.Delay(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Same(disposal, await Task.WhenAny(disposal, bound));
        var failure = await Record.ExceptionAsync(() => disposal);
        Assert.NotNull(failure);
        Assert.Equal("AppHost graph cleanup failed.", failure.Message);
        Assert.IsType<TaskCanceledException>(failure.InnerException);
        Assert.Equal(1, applicationHost.StopCalls);
        Assert.Equal(1, applicationHost.AsyncDisposeCalls);
        await AssertLeaseReleasedExactlyOnceAsync();
    }

    [Fact(DisplayName = "AppHost cleanup preserves the first failure and retains later failures")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "RunningAppHost owns the supplied distributed application.")]
    public async Task AppHostCleanupPreservesTheFirstFailureAndRetainsLaterFailures()
    {
        var primary = new InvalidOperationException("stop failed");
        var later = new IOException("dispose failed");
        var applicationHost = new ControllableHost(primary, later);
        var releases = 0;
        var running = await CreateRunningAppHost(
            applicationHost,
            _ => Interlocked.Increment(ref releases));

        var failure = await Record.ExceptionAsync(
            () => running.DisposeAsync().AsTask());

        Assert.NotNull(failure);
        Assert.Same(primary, failure.InnerException);
        var secondary = Assert.IsType<AggregateException>(
            primary.Data["AppHost cleanup secondary failures"]);
        Assert.Same(later, Assert.Single(secondary.InnerExceptions));
        Assert.Equal(1, applicationHost.StopCalls);
        Assert.Equal(1, applicationHost.AsyncDisposeCalls);
        Assert.Equal(1, releases);
        await AssertLeaseReleasedExactlyOnceAsync();
    }

    [Fact(DisplayName = "AppHost cleanup is idempotent")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "RunningAppHost owns the supplied distributed application.")]
    public async Task AppHostCleanupIsIdempotent()
    {
        var applicationHost = new ControllableHost();
        var releases = 0;
        var running = await CreateRunningAppHost(
            applicationHost,
            _ => Interlocked.Increment(ref releases));

        await running.DisposeAsync();
        await running.DisposeAsync();

        Assert.Equal(1, applicationHost.StopCalls);
        Assert.Equal(1, applicationHost.AsyncDisposeCalls);
        Assert.Equal(1, releases);
        await AssertLeaseReleasedExactlyOnceAsync();
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "RunningAppHost owns the supplied distributed application.")]
    private static async Task<RunningAppHost> CreateRunningAppHost(
        IHost applicationHost,
        Action<RunningAppHost>? release = null)
    {
        var application = new DistributedApplication(applicationHost);
        var lease = await AcquireLeaseAsync(CancellationToken.None);
        var constructor = typeof(RunningAppHost).GetConstructors(
                BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();
        return (RunningAppHost)constructor.Invoke(
        [
            application,
            lease,
            release ?? (_ => { }),
            TestCleanupBudget,
        ]);
    }

    private static async Task AssertLeaseReleasedExactlyOnceAsync()
    {
        using var timeout = new CancellationTokenSource(TestCleanupBudget);
        var lease = await AcquireLeaseAsync(timeout.Token);
        using var contenderTimeout = new CancellationTokenSource(TestCleanupBudget);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AcquireLeaseAsync(contenderTimeout.Token));
        await DisposeLeaseAsync(lease);
    }

    private static async Task<object> AcquireLeaseAsync(
        CancellationToken cancellationToken)
    {
        var leaseType = typeof(RunningAppHost).Assembly.GetType(
            "DigitalBrain.Testing.AppHostExclusiveLease",
            throwOnError: true)!;
        var acquire = leaseType.GetMethod(
            "AcquireAsync",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var leaseTask = (Task)acquire.Invoke(null, [cancellationToken])!;
        await leaseTask;
        return leaseTask.GetType().GetProperty("Result")!.GetValue(leaseTask)!;
    }

    private static Task DisposeLeaseAsync(object lease)
    {
        var dispose = lease.GetType().GetMethod(
            nameof(IAsyncDisposable.DisposeAsync),
            BindingFlags.Public | BindingFlags.Instance)!;
        var valueTask = dispose.Invoke(lease, null)!;
        return (Task)valueTask.GetType().GetMethod("AsTask")!.Invoke(
            valueTask,
            null)!;
    }

    private sealed class ControllableHost : IHost, IAsyncDisposable
    {
        private readonly Exception? _disposeFailure;
        private readonly bool _hangDuringDispose;
        private readonly ServiceProvider _services = CreateServices();
        private readonly Exception? _stopFailure;

        public ControllableHost(
            Exception? stopFailure = null,
            Exception? disposeFailure = null,
            bool hangDuringDispose = false)
        {
            _stopFailure = stopFailure;
            _disposeFailure = disposeFailure;
            _hangDuringDispose = hangDuringDispose;
        }

        public int AsyncDisposeCalls { get; private set; }

        public IServiceProvider Services => _services;

        public int StopCalls { get; private set; }

        public void Dispose() => _services.Dispose();

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCalls++;
            if (_hangDuringDispose)
            {
                return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan));
            }

            return _disposeFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(_disposeFailure);
        }

        public void Start()
        {
            GC.KeepAlive(_services);
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Stop()
        {
            GC.KeepAlive(_services);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return _stopFailure is null
                ? Task.CompletedTask
                : Task.FromException(_stopFailure);
        }

        private static ServiceProvider CreateServices()
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton(
                new DistributedApplicationModel(Array.Empty<IResource>()));
            builder.Services.AddSingleton<ResourceNotificationService>();
            builder.Services.AddSingleton<ResourceCommandService>();
            return builder.Services.BuildServiceProvider();
        }
    }
}
