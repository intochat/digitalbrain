using System.Reflection;
using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

[Signal("test.note")]
public sealed record Note(string Text) : Signal;

[Signal("test.count")]
public sealed record Count(int N) : Signal;

/// <summary>Plain state: no Orleans attributes.</summary>
public sealed record Memo(string Title, int Count, IReadOnlyList<string> Tags);

internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FixedCursors(long? lowest) : ISynapseCursorLookup
{
    public long? Lowest { get; set; } = lowest;

    public ValueTask<long?> LowestLive(string brainKey, NeuronId publisher) => ValueTask.FromResult(Lowest);
}

internal sealed class TestGrainState<T> : IGrainState<T>
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public T State { get; set; } = default!;

    public string? ETag { get; set; }

    public bool RecordExists { get; set; }
}

/// <summary>BrainLog services over a temp home, without a silo; <see cref="Reopen"/> simulates a restart.</summary>
internal sealed class BrainStore : IAsyncDisposable
{
    public static readonly NeuronId P = NeuronId.Parse("b1/chat/p");
    public static readonly NeuronId Q = NeuronId.Parse("b1/chat/q");

    private readonly string home;
    private readonly Action<IServiceCollection>? configure;
    private ServiceProvider provider;

    public BrainStore(string home, Action<IServiceCollection>? configure = null)
    {
        this.home = home;
        this.configure = configure;
        provider = Build();
    }

    public IServiceProvider Services => provider;

    internal BrainLogs Logs => provider.GetRequiredService<BrainLogs>();

    internal BrainGrainStorage Storage => (BrainGrainStorage)provider.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage);

    internal JournalPayloads Payloads => provider.GetRequiredService<JournalPayloads>();

    internal Task<BrainLog> Log(string brain = "b1") => Logs.For(brain);

    internal BrainSignalHistory History(string brain = "b1") => (BrainSignalHistory)provider.GetRequiredService<BrainSignalHistories>().For(new BrainId(brain));

    internal string DirectoryOf(string brain = "b1") => provider.GetRequiredService<BrainStorageKeys>().DirectoryOf(brain);

    public static GrainId GrainOf(NeuronId id) => GrainId.Create("neuron.host", id.ToString());

    /// <summary>Appends <paramref name="count"/> signals of <paramref name="publisher"/> in entries of <paramref name="batch"/>.</summary>
    internal async Task Publish(NeuronId publisher, int count, int batch = 100, Action<int>? beforeEntry = null)
    {
        var log = await Log(publisher.Brain.Value);
        var done = 0;
        var entry = 0;
        while (done < count)
        {
            beforeEntry?.Invoke(entry++);
            var size = Math.Min(batch, count - done);
            Signal[] signals = [.. Enumerable.Range(done + 1, size).Select(n => new Count(n))];
            await log.AppendEntry(GrainOf(publisher).ToString(), "", null, BrainRecordChange.None, [], publisher.ToString(), signals, CancellationToken.None);
            done += size;
        }
    }

    public async Task Reopen()
    {
        await provider.DisposeAsync();
        provider = Build();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IntoChatOSNames.HomeKey] = home,
            [IntoChatOSNames.MasterKeyKey] = "test-master-key-0123456789",
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSerializer(serializer => serializer.AddAssembly(typeof(BrainLog).Assembly));
        configure?.Invoke(services);
        BrainStorageHosting.AddBrainStorageServices(services, [typeof(Note).Assembly]);
        return services.BuildServiceProvider();
    }

    internal static IOptions<RetentionOptions> Window(int signals, TimeSpan? age = null)
        => Options.Create(new RetentionOptions { HistoryWindowSignals = signals, HistoryWindowAge = age ?? TimeSpan.FromDays(7) });

    internal static Assembly TestAssembly => typeof(Note).Assembly;
}
