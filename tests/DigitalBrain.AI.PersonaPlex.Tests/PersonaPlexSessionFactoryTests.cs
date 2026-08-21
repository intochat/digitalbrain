using DigitalBrain.AI.PersonaPlex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.AI.PersonaPlex.Tests;

public sealed class PersonaPlexSessionFactoryTests
{
    [Fact]
    public void CudaSessionSettingsDisableCpuExecutionProviderFallback()
    {
        var settings = PersonaPlexOrtSessionSettings.Create(cudaDeviceId: 2);

        Assert.Equal("1", settings.SessionConfigEntries["session.disable_cpu_ep_fallback"]);
        Assert.Equal("2", settings.ProviderOptions["device_id"]);
    }

    [Fact]
    public async Task DisabledConfigurationReportsUnavailableWithoutOpeningOrtSessions()
    {
        await using var factory = new PersonaPlexSessionFactory(
            Options.Create(new PersonaPlexOptions
            {
                Enabled = false,
                ModelDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            }),
            NullLogger<PersonaPlexSessionFactory>.Instance);

        await factory.StartAsync(CancellationToken.None);

        Assert.Equal(PersonaPlexReadinessState.Disabled, factory.Readiness.State);
        Assert.False(factory.Readiness.IsModelConfigurationValid);
    }

    [Fact]
    public async Task DisabledConfigurationRejectsSessionCreation()
    {
        await using var factory = new PersonaPlexSessionFactory(
            Options.Create(new PersonaPlexOptions { Enabled = false }),
            NullLogger<PersonaPlexSessionFactory>.Instance);

        async Task CreateSessionAsync() =>
            await factory.CreateAsync(new PersonaPlexSessionRequest("connection-1"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(CreateSessionAsync);

        Assert.Equal("PersonaPlex is disabled.", exception.Message);
    }

    [Fact]
    public async Task ConfiguredMaxSessionsAtomicallyRejectsAdditionalLiveSessions()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var options = new PersonaPlexOptions
            {
                Enabled = true,
                ModelDirectory = modelDirectory,
                MaxSessions = 1,
            };

            var modelSet = new InstantSessionModelSet();
            await using var factory = new PersonaPlexSessionFactory(
                Options.Create(options),
                NullLogger<PersonaPlexSessionFactory>.Instance,
                _ => modelSet);
            await factory.StartAsync(CancellationToken.None);
            var testCancellation = TestContext.Current.CancellationToken;

            var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                try
                {
                    return new SessionAttempt(
                        await factory.CreateAsync(
                            new PersonaPlexSessionRequest($"connection-{index}"),
                            testCancellation),
                        null);
                }
                catch (Exception exception)
                {
                    return new SessionAttempt(null, exception);
                }
            }));

            var grantedSessions = attempts
                .Where(static attempt => attempt.Session is not null)
                .Select(static attempt => attempt.Session!)
                .ToArray();
            var rejectedAttempts = attempts.Where(static attempt => attempt.Exception is not null).ToArray();

            Assert.Single(grantedSessions);
            Assert.Equal(7, rejectedAttempts.Length);
            Assert.Equal(1, modelSet.SessionCreationCount);
            Assert.All(
                rejectedAttempts,
                attempt => Assert.Equal("PersonaPlex session limit has been reached.", attempt.Exception!.Message));

            await grantedSessions[0].DisposeAsync();
            await using var replacement = await factory.CreateAsync(
                new PersonaPlexSessionRequest("replacement"),
                testCancellation);
            Assert.Equal(2, modelSet.SessionCreationCount);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public void ModelManifestRejectsTemporalGraphWithoutDeviceCacheOutputs()
    {
        var inputs = new HashSet<string>(["input_frame", "attention_mask"]);
        var outputs = new HashSet<string>(["hidden", "text_logits"]);
        for (var layer = 0; layer < 32; layer++)
        {
            inputs.Add($"past_key_values.{layer}.key");
            inputs.Add($"past_key_values.{layer}.value");
        }

        void Validate() => PersonaPlexModelManifest.ValidateTemporalNames(inputs, outputs);

        var exception = Assert.Throws<PersonaPlexModelManifestException>(Validate);

        Assert.Contains("present.31.value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedGraphsAreReportedAsInvalidModelConfigurationWithoutExposingTheModelPath()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            await using var factory = new PersonaPlexSessionFactory(
                Options.Create(new PersonaPlexOptions
                {
                    Enabled = true,
                    ModelDirectory = modelDirectory,
                }),
                NullLogger<PersonaPlexSessionFactory>.Instance);

            await factory.StartAsync(CancellationToken.None);

            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
            Assert.False(factory.Readiness.IsModelConfigurationValid);
            Assert.Equal(
                "PersonaPlex model set failed validation or CUDA initialization.",
                factory.Readiness.Message);
            Assert.DoesNotContain(modelDirectory, factory.Readiness.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task HostingRegistersOneFactoryForTheContractAndWarmupLifecycle()
    {
        var configuration = new ConfigurationManager
        {
            [$"{PersonaPlexOptions.SectionName}:Enabled"] = "false",
            [$"{PersonaPlexOptions.SectionName}:MaxSessions"] = "2",
        };
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPersonaPlex(configuration);

        await using var provider = services.BuildServiceProvider();
        var concreteFactory = provider.GetRequiredService<PersonaPlexSessionFactory>();

        Assert.Same(concreteFactory, provider.GetRequiredService<IPersonaPlexSessionFactory>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => ReferenceEquals(service, concreteFactory));
        Assert.Equal(2, provider.GetRequiredService<IOptions<PersonaPlexOptions>>().Value.MaxSessions);
    }

    [Fact]
    public async Task StopDrainsLiveSessionsBeforeDisposingModelSetAndRejectsNewSessions()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var modelSet = new ControllableModelSet();
            await using var factory = CreateFactoryWithModelSet(modelDirectory, modelSet);
            await factory.StartAsync(CancellationToken.None);
            var testCancellation = TestContext.Current.CancellationToken;
            var session = await factory.CreateAsync(
                new PersonaPlexSessionRequest("connection-1"),
                testCancellation);
            var processTask = session.ProcessAsync(
                PersonaPlexAudioFrame.Create(1, new short[1920]),
                testCancellation).AsTask();
            await modelSet.Session.ProcessStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                testCancellation);

            var stopTask = factory.StopAsync(CancellationToken.None);
            await modelSet.Session.DisposeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                testCancellation);

            Assert.False(modelSet.Disposed);

            async Task CreateDuringStopAsync() =>
                await factory.CreateAsync(new PersonaPlexSessionRequest("connection-2"), testCancellation);

            await Assert.ThrowsAsync<InvalidOperationException>(CreateDuringStopAsync);

            modelSet.Session.AllowCompletion.TrySetResult();
            await processTask;
            await stopTask;

            Assert.True(modelSet.DisposedAfterSession);
            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
            Assert.True(factory.Readiness.IsModelConfigurationValid);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StopAwaitsSessionDisposalAlreadyInProgressBeforeDisposingModelSet()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var modelSet = new ControllableModelSet();
            await using var factory = CreateFactoryWithModelSet(modelDirectory, modelSet);
            await factory.StartAsync(CancellationToken.None);
            var session = await factory.CreateAsync(
                new PersonaPlexSessionRequest("connection-1"),
                TestContext.Current.CancellationToken);

            var sessionDisposeTask = session.DisposeAsync().AsTask();
            var stopTask = factory.StopAsync(CancellationToken.None);

            Assert.True(modelSet.Session.DisposeStarted.Task.IsCompleted);
            Assert.False(modelSet.Disposed);

            modelSet.Session.AllowCompletion.TrySetResult();
            await sessionDisposeTask;
            await stopTask;

            Assert.True(modelSet.DisposedAfterSession);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledStopStillDrainsSessionsAndDisposesModelSet()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var modelSet = new ControllableModelSet();
            await using var factory = CreateFactoryWithModelSet(modelDirectory, modelSet);
            await factory.StartAsync(CancellationToken.None);
            await factory.CreateAsync(
                new PersonaPlexSessionRequest("connection-1"),
                TestContext.Current.CancellationToken);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            var stopTask = factory.StopAsync(cancellationSource.Token);
            var firstCompletion = await Task.WhenAny(
                modelSet.Session.DisposeStarted.Task,
                stopTask);

            modelSet.Session.AllowCompletion.TrySetResult();
            var stopException = await Record.ExceptionAsync(() => stopTask);

            Assert.Same(modelSet.Session.DisposeStarted.Task, firstCompletion);
            Assert.Null(stopException);
            Assert.True(modelSet.DisposedAfterSession);
            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
            Assert.Equal("PersonaPlex runtime is stopped.", factory.Readiness.Message);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeDrainsLiveSessionsBeforeDisposingModelSetAndMovesAwayFromReady()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var modelSet = new ControllableModelSet();
            var factory = CreateFactoryWithModelSet(modelDirectory, modelSet);
            await factory.StartAsync(CancellationToken.None);
            var testCancellation = TestContext.Current.CancellationToken;
            var session = await factory.CreateAsync(
                new PersonaPlexSessionRequest("connection-1"),
                testCancellation);
            var processTask = session.ProcessAsync(
                PersonaPlexAudioFrame.Create(1, new short[1920]),
                testCancellation).AsTask();
            await modelSet.Session.ProcessStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                testCancellation);

            var disposeTask = factory.DisposeAsync().AsTask();

            try
            {
                var firstCompletion = await Task.WhenAny(
                    modelSet.Session.DisposeStarted.Task,
                    disposeTask);

                Assert.Same(modelSet.Session.DisposeStarted.Task, firstCompletion);
                Assert.NotEqual(PersonaPlexReadinessState.Ready, factory.Readiness.State);
                Assert.False(modelSet.Disposed);
            }
            finally
            {
                modelSet.Session.AllowCompletion.TrySetResult();
                await processTask;
                await disposeTask;
            }

            Assert.True(modelSet.DisposedAfterSession);
            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationDuringWarmupTransitionsReadinessToFailed()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var modelSet = new CancelingWarmupModelSet();
            await using var factory = CreateFactoryWithModelSet(modelDirectory, modelSet);
            using var cancellationSource = new CancellationTokenSource();

            var startTask = factory.StartAsync(cancellationSource.Token);
            await modelSet.WarmupStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await cancellationSource.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
            Assert.True(factory.Readiness.IsModelConfigurationValid);
            Assert.Equal("PersonaPlex startup was canceled.", factory.Readiness.Message);
            Assert.True(modelSet.Disposed);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationBeforeStartupBeginsTransitionsReadinessToFailed()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var modelSet = new ControllableModelSet();
            await using var factory = CreateFactoryWithModelSet(modelDirectory, modelSet);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => factory.StartAsync(cancellationSource.Token));

            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
            Assert.False(factory.Readiness.IsModelConfigurationValid);
            Assert.Equal("PersonaPlex startup was canceled.", factory.Readiness.Message);
            Assert.False(modelSet.Disposed);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StopDuringWarmupPreventsReadyPublicationSessionAdmissionAndRestart()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var logger = new ReadinessRecordingLogger();
            var modelSet = new GatedWarmupModelSet();
            await using var factory = CreateFactoryWithModelSet(modelDirectory, modelSet, logger);
            logger.ReadReadiness = () => factory.Readiness;
            var testCancellation = TestContext.Current.CancellationToken;

            var startTask = factory.StartAsync(CancellationToken.None);
            await modelSet.WarmupStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                testCancellation);

            var stopTask = factory.StopAsync(CancellationToken.None);

            async Task CreateDuringStopAsync() =>
                await factory.CreateAsync(new PersonaPlexSessionRequest("connection-1"), testCancellation);

            await Assert.ThrowsAsync<InvalidOperationException>(CreateDuringStopAsync);
            modelSet.AllowWarmupCompletion.TrySetResult();
            await startTask;
            await stopTask;

            Assert.DoesNotContain(PersonaPlexReadinessState.Ready, logger.States);
            Assert.Equal(0, modelSet.SessionCreationCount);
            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);

            async Task RestartAsync() => await factory.StartAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(RestartAsync);
            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeDuringWarmupPreventsReadyPublicationAndSessionAdmission()
    {
        var modelDirectory = CreateInvalidFourGraphDirectory();
        try
        {
            var logger = new ReadinessRecordingLogger();
            var modelSet = new GatedWarmupModelSet();
            var factory = CreateFactoryWithModelSet(modelDirectory, modelSet, logger);
            logger.ReadReadiness = () => factory.Readiness;
            var testCancellation = TestContext.Current.CancellationToken;

            var startTask = factory.StartAsync(CancellationToken.None);
            await modelSet.WarmupStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                testCancellation);

            var disposeTask = factory.DisposeAsync().AsTask();

            async Task CreateDuringDisposeAsync() =>
                await factory.CreateAsync(new PersonaPlexSessionRequest("connection-1"), testCancellation);

            var createException = await Record.ExceptionAsync(CreateDuringDisposeAsync);
            modelSet.AllowWarmupCompletion.TrySetResult();
            await startTask;
            await disposeTask;

            Assert.DoesNotContain(PersonaPlexReadinessState.Ready, logger.States);
            Assert.IsType<ObjectDisposedException>(createException);
            Assert.Equal(0, modelSet.SessionCreationCount);
            Assert.Equal(PersonaPlexReadinessState.Failed, factory.Readiness.State);

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => factory.StartAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(modelDirectory, recursive: true);
        }
    }

    private static PersonaPlexSessionFactory CreateFactoryWithModelSet(
        string modelDirectory,
        IPersonaPlexModelSet modelSet,
        ILogger<PersonaPlexSessionFactory>? logger = null)
        => new(
            Options.Create(new PersonaPlexOptions
            {
                Enabled = true,
                ModelDirectory = modelDirectory,
            }),
            logger ?? NullLogger<PersonaPlexSessionFactory>.Instance,
            _ => modelSet);

    private static string CreateInvalidFourGraphDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"personaplex-invalid-{Guid.NewGuid():N}");
        foreach (var graph in new[] { "mimi_encoder", "temporal", "depformer", "mimi_decoder" })
        {
            var graphDirectory = Directory.CreateDirectory(Path.Combine(directory, graph));
            File.WriteAllBytes(Path.Combine(graphDirectory.FullName, "model.onnx"), [0]);
        }

        return directory;
    }

    private sealed class ControllableModelSet : IPersonaPlexModelSet
    {
        public BlockingSession Session { get; } = new();

        public bool Disposed { get; private set; }

        public bool DisposedAfterSession { get; private set; }

        public IPersonaPlexSession CreateSession() => Session;

        public ValueTask WarmUpAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose()
        {
            DisposedAfterSession = Session.Disposed;
            Disposed = true;
        }
    }

    private sealed class InstantSessionModelSet : IPersonaPlexModelSet
    {
        private int _sessionCreationCount;

        public int SessionCreationCount => Volatile.Read(ref _sessionCreationCount);

        public IPersonaPlexSession CreateSession()
        {
            Interlocked.Increment(ref _sessionCreationCount);
            return new InstantSession();
        }

        public ValueTask WarmUpAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class InstantSession : IPersonaPlexSession
    {
        public ValueTask<PersonaPlexAudioFrame> ProcessAsync(
            PersonaPlexAudioFrame frame,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(frame);

        public ValueTask ResetAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record SessionAttempt(IPersonaPlexSession? Session, Exception? Exception);

    private sealed class BlockingSession : IPersonaPlexSession
    {
        public TaskCompletionSource AllowCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ProcessStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public async ValueTask<PersonaPlexAudioFrame> ProcessAsync(
            PersonaPlexAudioFrame frame,
            CancellationToken cancellationToken = default)
        {
            ProcessStarted.TrySetResult();
            await AllowCompletion.Task.WaitAsync(cancellationToken);
            return frame;
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            await AllowCompletion.Task;
            Disposed = true;
        }
    }

    private sealed class CancelingWarmupModelSet : IPersonaPlexModelSet
    {
        public TaskCompletionSource WarmupStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public IPersonaPlexSession CreateSession() => throw new NotSupportedException();

        public async ValueTask WarmUpAsync(CancellationToken cancellationToken)
        {
            WarmupStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class GatedWarmupModelSet : IPersonaPlexModelSet
    {
        private int _sessionCreationCount;

        public TaskCompletionSource AllowWarmupCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WarmupStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SessionCreationCount => Volatile.Read(ref _sessionCreationCount);

        public IPersonaPlexSession CreateSession()
        {
            Interlocked.Increment(ref _sessionCreationCount);
            return new BlockingSession();
        }

        public async ValueTask WarmUpAsync(CancellationToken cancellationToken)
        {
            WarmupStarted.TrySetResult();
            await AllowWarmupCompletion.Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReadinessRecordingLogger : ILogger<PersonaPlexSessionFactory>
    {
        private readonly List<PersonaPlexReadinessState> _states = [];
        private readonly Lock _statesLock = new();

        public Func<PersonaPlexReadiness>? ReadReadiness { get; set; }

        public IReadOnlyList<PersonaPlexReadinessState> States
        {
            get
            {
                lock (_statesLock)
                {
                    return [.. _states];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var readiness = ReadReadiness?.Invoke();
            if (readiness is null)
            {
                return;
            }

            lock (_statesLock)
            {
                _states.Add(readiness.State);
            }
        }
    }
}
