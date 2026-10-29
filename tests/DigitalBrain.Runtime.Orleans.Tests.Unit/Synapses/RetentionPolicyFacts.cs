using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Synapses;

public sealed class RetentionPolicyFacts
{
    private static readonly NeuronId Publisher = NeuronId.Parse("b1/chat/p");

    private sealed class FakeLookup(long? lowest) : ISynapseCursorLookup
    {
        public ValueTask<long?> LowestLive(string brainKey, NeuronId publisher) => ValueTask.FromResult(lowest);
    }

    private static RetentionPolicy Policy(long? lowest, RetentionOptions? options = null)
        => new(Options.Create(options ?? new RetentionOptions()), TimeProvider.System, new FakeLookup(lowest));

    private static readonly RetentionOptions Thousand = new() { HistoryWindowSignals = 1000 };

    // Ages of the entries, newest last; countNewerThan counts entries younger than the window (unknown time counts as newer).
    private static Func<TimeSpan, long> Ages(IReadOnlyList<TimeSpan?> ages) => window => ages.Count(a => a is null || a < window);

    private static Func<TimeSpan, long> NoAge => _ => 0;

    [Fact]
    public async Task Signal_window_floor_is_last_minus_window_when_the_lowest_cursor_is_ahead()
        => Assert.Equal(4000, await Policy(4500, Thousand).FloorFor("b1", Publisher, 5000, NoAge, TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_lower_live_cursor_pins_the_floor()
        => Assert.Equal(3000, await Policy(3000, Thousand).FloorFor("b1", Publisher, 5000, NoAge, TestContext.Current.CancellationToken));

    [Fact]
    public async Task No_live_cursor_leaves_the_window_floor()
        => Assert.Equal(4000, await Policy(null, Thousand).FloorFor("b1", Publisher, 5000, NoAge, TestContext.Current.CancellationToken));

    [Fact]
    public async Task The_floor_is_never_negative()
        => Assert.Equal(0, await Policy(null).FloorFor("b1", Publisher, 50, NoAge, TestContext.Current.CancellationToken));

    [Fact]
    public async Task Twenty_thousand_signals_within_one_day_are_all_kept()
    {
        var ages = Enumerable.Repeat<TimeSpan?>(TimeSpan.FromHours(12), 20_000).ToList();
        Assert.Equal(0, await Policy(null).FloorFor("b1", Publisher, 20_000, Ages(ages), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Twenty_thousand_signals_older_than_seven_days_keep_the_newest_ten_thousand()
    {
        var ages = Enumerable.Repeat<TimeSpan?>(TimeSpan.FromDays(8), 20_000).ToList();
        Assert.Equal(10_000, await Policy(null).FloorFor("b1", Publisher, 20_000, Ages(ages), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Five_hundred_signals_over_thirty_days_are_all_kept()
    {
        var ages = Enumerable.Range(0, 500).Select(i => (TimeSpan?)TimeSpan.FromDays(30.0 * i / 500)).ToList();
        Assert.Equal(0, await Policy(null).FloorFor("b1", Publisher, 500, Ages(ages), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entries_with_unknown_time_count_as_newer_than_the_age_window()
    {
        var ages = Enumerable.Repeat<TimeSpan?>(null, 1500).ToList();
        Assert.Equal(0, await Policy(null, Thousand).FloorFor("b1", Publisher, 1500, Ages(ages), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_default_policy_has_no_live_cursors()
        => Assert.Equal(4000, await new DefaultRetentionPolicy(Options.Create(Thousand)).FloorFor("b1", Publisher, 5000, NoAge, TestContext.Current.CancellationToken));

    [Fact]
    public void Options_default_to_seven_days_ten_thousand_signals_and_seven_days()
    {
        var o = new RetentionOptions();
        Assert.Equal((TimeSpan.FromDays(7), 10_000, TimeSpan.FromDays(7)), (o.HistoryWindowAge, o.HistoryWindowSignals, o.StaleCursorAfter));
    }

    [Fact]
    public void Options_bind_from_the_retention_section()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IntoChatOSNames.Signalling.Retention + ":HistoryWindowAge"] = "1.00:00:00",
            [IntoChatOSNames.Signalling.Retention + ":HistoryWindowSignals"] = "42",
            [IntoChatOSNames.Signalling.Retention + ":StaleCursorAfter"] = "02:00:00",
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(config);
        SynapseRetentionHosting.AddRetentionOptions(services);
        var o = services.BuildServiceProvider().GetRequiredService<IOptions<RetentionOptions>>().Value;
        Assert.Equal((TimeSpan.FromDays(1), 42, TimeSpan.FromHours(2)), (o.HistoryWindowAge, o.HistoryWindowSignals, o.StaleCursorAfter));
        Assert.Equal("IntoChatOS:Signalling:Retention", IntoChatOSNames.Signalling.Retention);
    }
}
