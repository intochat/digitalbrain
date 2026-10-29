using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class FileReminderTableFacts : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "db-frt-" + Guid.NewGuid().ToString("N"), "state", FileReminderTable.FileName);

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(_file))!;
        if (Directory.Exists(folder)) { Directory.Delete(folder, recursive: true); }
    }

    private FileReminderTable Table() => new(_file);

    private static ReminderEntry Entry(string key, string name = "r") => new()
    {
        GrainId = GrainId.Create("probe", key),
        ReminderName = name,
        StartAt = new DateTime(2026, 10, 25, 12, 0, 0, 500, DateTimeKind.Utc),
        Period = TimeSpan.FromMinutes(1),
    };

    [Fact]
    public async Task Upsert_returns_a_non_empty_etag_that_changes_per_upsert()
    {
        using var table = Table();
        var first = await table.UpsertRow(Entry("a"));
        var second = await table.UpsertRow(Entry("a"));
        Assert.False(string.IsNullOrEmpty(first));
        Assert.NotEqual(first, second);
        Assert.Single((await table.ReadRows(GrainId.Create("probe", "a"))).Reminders);
    }

    [Fact]
    public async Task StartAt_is_stored_as_utc_whole_seconds()
    {
        using var table = Table();
        await table.UpsertRow(Entry("a"));
        var row = await table.ReadRow(GrainId.Create("probe", "a"), "r");
        Assert.NotNull(row);
        Assert.Equal(DateTimeKind.Utc, row.StartAt.Kind);
        Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), row.StartAt);
    }

    [Theory]
    [InlineData(10u, 20u, 15u, true)]
    [InlineData(10u, 20u, 10u, false)]
    [InlineData(10u, 20u, 20u, true)]
    [InlineData(20u, 10u, 5u, true)]
    [InlineData(20u, 10u, 25u, true)]
    [InlineData(20u, 10u, 15u, false)]
    [InlineData(0u, 0u, 0u, true)]
    [InlineData(0u, 0u, 123u, true)]
    public void Ranges_are_begin_exclusive_end_inclusive_with_wrap(uint begin, uint end, uint hash, bool expected)
        => Assert.Equal(expected, FileReminderTable.InRange(hash, begin, end));

    [Fact]
    public async Task Zero_to_zero_reads_every_row()
    {
        using var table = Table();
        await table.UpsertRow(Entry("a"));
        await table.UpsertRow(Entry("b"));
        await table.UpsertRow(Entry("c"));
        Assert.Equal(3, (await table.ReadRows(0, 0)).Reminders.Count);
    }

    [Fact]
    public async Task Remove_with_a_stale_etag_returns_false_and_keeps_the_row()
    {
        using var table = Table();
        var etag = await table.UpsertRow(Entry("a"));
        Assert.False(await table.RemoveRow(GrainId.Create("probe", "a"), "r", "stale"));
        Assert.True(await table.RemoveRow(GrainId.Create("probe", "a"), "r", etag!));
        Assert.Null(await table.ReadRow(GrainId.Create("probe", "a"), "r"));
    }

    [Fact]
    public async Task Start_is_idempotent_and_rows_survive_a_new_instance()
    {
        using (var table = Table())
        {
            await table.StartAsync(TestContext.Current.CancellationToken);
            await table.StartAsync(TestContext.Current.CancellationToken);
            await table.UpsertRow(Entry("a", "keep"));
        }
        using var reopened = Table();
        await reopened.StartAsync(TestContext.Current.CancellationToken);
        var row = await reopened.ReadRow(GrainId.Create("probe", "a"), "keep");
        Assert.NotNull(row);
        Assert.Equal(TimeSpan.FromMinutes(1), row.Period);
    }
}
