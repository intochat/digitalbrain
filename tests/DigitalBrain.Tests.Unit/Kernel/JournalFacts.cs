using System.Text;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class JournalFacts
{
    [Fact]
    public void Decide_and_apply_advances_sequence_and_position()
    {
        var journal = new Journal<string>();
        var bytes = Encoding.UTF8.GetBytes("{\"value\":42}");
        var state = new RecordWrite("state", StateChange.Write, bytes, null);

        var entry = journal.Decide("brain/test/1", state, null, "brain/test/1", ["sig1", "sig2"], 1000);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(1, entry.FirstSignal);
        Assert.Equal(1, entry.FirstPosition);
        Assert.Equal(2, entry.Signals.Count);

        journal.Apply(entry);
        Assert.Equal(1, journal.Sequence);
        Assert.Equal(2, journal.Position);
        Assert.Equal(2, journal.LastOf("brain/test/1"));

        var record = journal.Record("brain/test/1", "state");
        Assert.NotNull(record);
        Assert.Equal(1, record.Version);
        Assert.Equal(bytes, record.Bytes.ToArray());
    }

    [Fact]
    public void Decide_checks_expected_version_and_throws_StaleVersionException_on_mismatch()
    {
        var journal = new Journal<string>();
        var bytes = Encoding.UTF8.GetBytes("{}");
        var state = new RecordWrite("state", StateChange.Write, bytes, null);
        journal.Apply(journal.Decide("brain/test/1", state, null, "", [], 1000));

        // Now expected version is 1, but we pass 99
        var staleWrite = new RecordWrite("state", StateChange.Write, bytes, 99);
        var ex = Assert.Throws<StaleVersionException>(() => journal.Decide("brain/test/1", staleWrite, null, "", [], 2000));
        Assert.Equal("brain/test/1", ex.Owner);
        Assert.Equal("state", ex.Name);
        Assert.Equal(1, ex.Stored);
        Assert.Equal(99, ex.Expected);
    }

    [Fact]
    public void Non_contiguous_apply_throws_InvalidDataException()
    {
        var journal = new Journal<string>();
        var entry = new JournalEntry<string>(5, "owner", null, null, "", [], 1, 1, 1000); // Sequence should be 1
        Assert.Throws<InvalidDataException>(() => journal.Apply(entry));
    }

    [Fact]
    public void Clear_state_removes_record()
    {
        var journal = new Journal<string>();
        var bytes = Encoding.UTF8.GetBytes("{}");
        journal.Apply(journal.Decide("brain/test/1", new RecordWrite("state", StateChange.Write, bytes, null), null, "", [], 1000));
        Assert.NotNull(journal.Record("brain/test/1", "state"));

        journal.Apply(journal.Decide("brain/test/1", new RecordWrite("state", StateChange.Clear, ReadOnlyMemory<byte>.Empty, 1), null, "", [], 2000));
        Assert.Null(journal.Record("brain/test/1", "state"));
    }

    [Fact]
    public void HeldAfter_and_Trim_manage_signals_and_floors()
    {
        var journal = new Journal<string>();
        journal.Apply(journal.Decide("b/t/1", null, null, "b/t/1", ["s1", "s2"], 1000));
        journal.Apply(journal.Decide("b/t/2", null, null, "b/t/2", ["s3"], 2000));

        var allHeld = journal.HeldAfter(0, null, 10).ToList();
        Assert.Equal(2, allHeld.Count);

        var pub2Held = journal.HeldAfter(0, "b/t/2", 10).ToList();
        Assert.Single(pub2Held);
        Assert.Equal("b/t/2", pub2Held[0].Publisher);

        journal.Trim("b/t/1", floorSequence: 2, floorPosition: 2);
        Assert.Equal(2, journal.FloorOf("b/t/1"));

        var afterTrim = journal.HeldAfter(0, "b/t/1", 10).ToList();
        Assert.Empty(afterTrim);
    }

    [Fact]
    public void Snapshot_and_Restore_restores_identical_state()
    {
        var journal = new Journal<string>();
        var bytes = Encoding.UTF8.GetBytes("{\"k\":\"v\"}");
        journal.Apply(journal.Decide("b/t/1", new RecordWrite("state", StateChange.Write, bytes, null), null, "b/t/1", ["s1"], 1000));

        var records = journal.SnapshotRecords();
        var marks = journal.SnapshotMarks();
        var held = journal.HeldEntries.ToList();

        var restored = new Journal<string>();
        restored.Restore(journal.Sequence, journal.Position, records, marks, held);

        Assert.Equal(journal.Sequence, restored.Sequence);
        Assert.Equal(journal.Position, restored.Position);
        Assert.Equal(journal.LastOf("b/t/1"), restored.LastOf("b/t/1"));
        Assert.NotNull(restored.Record("b/t/1", "state"));
    }
}
