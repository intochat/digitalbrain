using DigitalBrain.InoLang.Tests;
using DigitalBrain.Runtime;
using System.Collections.Concurrent;

namespace DigitalBrain.InoLang.Tests.Internals;

internal static class TestDigitalBrainBootstrapper
{
    static readonly ConcurrentDictionary<TestDigitalBrainOptionsKey, Lazy<Task<TestDigitalBrain>>> Boots = new();
    static int _shutdownStarted;

    static TestDigitalBrainBootstrapper()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                ShutdownIfBootedAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
            }
        };
    }

    public static Task<TestDigitalBrain> GetAsync() => GetAsync(DefaultOptions());

    public static Task<TestDigitalBrain> GetAsync(TestDigitalBrainOptions options)
    {
        var snapshot = options.Snapshot();
        if (snapshot.ParallelIsolation)
        {
            return TestDigitalBrain.BootAsync(snapshot);
        }

        if (Boots.IsEmpty && Volatile.Read(ref _shutdownStarted) == 1)
            Interlocked.Exchange(ref _shutdownStarted, 0);

        var key = TestDigitalBrainOptionsKey.From(snapshot);
        var boot = Boots.GetOrAdd(
            key,
            _ => new Lazy<Task<TestDigitalBrain>>(
                () => TestDigitalBrain.BootAsync(snapshot),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return boot.Value;
    }

    public static async ValueTask ShutdownIfBootedAsync()
    {
        if (Boots.IsEmpty) return;
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1) return;

        var boots = Boots.ToArray();
        Boots.Clear();

        List<Exception>? failures = null;
        foreach (var (_, boot) in boots)
        {
            if (!boot.IsValueCreated) continue;

            try
            {
                var harness = await boot.Value.ConfigureAwait(false);
                await harness.ShutdownAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures ??= [];
                failures.Add(ex);
            }
        }

        if (failures is null) return;
        if (failures.Count == 1) throw failures[0];
        throw new AggregateException("One or more DigitalBrain test harnesses failed to shut down.", failures);
    }

    static TestDigitalBrainOptions DefaultOptions() => new TestDigitalBrainOptions().WithMockedLlm();

    readonly record struct TestDigitalBrainOptionsKey(string Environment)
    {
        public static TestDigitalBrainOptionsKey From(TestDigitalBrainOptions options)
        {
            var environment = options.EnvironmentOverrides
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}\u001f{kvp.Value}");
            return new TestDigitalBrainOptionsKey(string.Join('\u001e', environment));
        }
    }
}
