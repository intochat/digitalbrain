using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

/// <summary>
/// Pins the persisted bytes of the BrainLog <c>Json</c> format (04-05, SAFE-01 clean break): the segment header and the Orleans
/// framing of an entry and a snapshot whose signal and state payloads are JSON. A change here is a format change.
/// R4-09 (04-16) added the entry's side state (a reacting subscriber's cursors) as two new fields within the same <c>Json</c> format:
/// the reader tolerates entries and snapshots written before them, so the earlier bytes stay pinned as readable. 04-17 added, the
/// same way, the state type name of an opaque record an adapter writes through IBrainJournalWriter (entry and snapshot record).
/// </summary>
public sealed class GoldenBytesFacts
{
    internal const string SegmentHeader = "44424C4702";
    internal const string Entry =
        "20E8001D412B6E6575726F6E2E686F73742F62312F636861742F70410B737461746509030541437B227469746C65223A226D222C22636F756E74223A312C2274616773223A5B5D7D411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0C101C101C101E0";

    internal const string Snapshot =
        "20E8001D012D213001312CD42E1D537472696E67436F6D706172657200030101E00103412B6E6575726F6E2E686F73742F62312F636861742F7020380300030101E00103410B737461746520E8400F7B226E223A317D011DC101E0E0E021000321E8001D4101C1310903014101411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0C101C101C101E0E021000321E8C0390109010DE0E0E0";

    /// <summary>An entry with a side state: the subscriber's synapse cursors written with its reaction (R4-09).</summary>
    internal const string EntryWithCursor =
        "20E8001D412B6E6575726F6E2E686F73742F62312F636861742F70410B737461746509030541437B227469746C65223A226D222C22636F756E74223A312C2274616773223A5B5D7D411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0411173796E6170736573411D7B22536F7572636573223A5B5D7DC101E0";

    /// <summary>An entry an adapter wrote through IBrainJournalWriter (04-17): an opaque record with its state type name.</summary>
    internal const string EntryWithRecordType =
        "20E8001D412B6E6575726F6E2E686F73742F62312F636861742F70410B737461746509030541070102FF411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0C101C1014139496E746F436861742E4F532E4C65676163792E436861745374617465E0";

    /// <summary>The entry, cursor entry and snapshot bytes as written before 04-17 added the record type; they must still read.</summary>
    internal const string EntryBeforeRecordType =
        "20E8001D412B6E6575726F6E2E686F73742F62312F636861742F70410B737461746509030541437B227469746C65223A226D222C22636F756E74223A312C2274616773223A5B5D7D411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0C101C101E0";

    internal const string EntryWithCursorBeforeRecordType =
        "20E8001D412B6E6575726F6E2E686F73742F62312F636861742F70410B737461746509030541437B227469746C65223A226D222C22636F756E74223A312C2274616773223A5B5D7D411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0411173796E6170736573411D7B22536F7572636573223A5B5D7DE0";

    internal const string SnapshotBeforeRecordType =
        "20E8001D012D213001312CD42E1D537472696E67436F6D706172657200030101E00103412B6E6575726F6E2E686F73742F62312F636861742F7020380300030101E00103410B737461746520E8400F7B226E223A317D011DE0E0E021000321E8001D4101C12F0903014101411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0C101C101E0E021000321E8C0370109010DE0E0E0";

    /// <summary>The entry and snapshot bytes as written before R4-09; they must still read.</summary>
    internal const string EntryBeforeSideState =
        "20E8001D412B6E6575726F6E2E686F73742F62312F636861742F70410B737461746509030541437B227469746C65223A226D222C22636F756E74223A312C2274616773223A5B5D7D411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0E0";

    internal const string SnapshotBeforeSideState =
        "20E8001D012D213001312CD42E1D537472696E67436F6D706172657200030101E00103412B6E6575726F6E2E686F73742F62312F636861742F7020380300030101E00103410B737461746520E8400F7B226E223A317D011DE0E0E021000321E8001D4101C12F0903014101411362312F636861742F7021000321E84013746573742E6E6F7465411B7B2274657874223A226869227DE0E0010D012D012000B22DB8D0E0E021000321E8C0370109010DE0E0E0";

    private static Serializer Serializer()
        => new ServiceCollection().AddSerializer(s => s.AddAssembly(typeof(BrainLog).Assembly)).BuildServiceProvider().GetRequiredService<Serializer>();

    private static readonly JournaledSignalPayload NotePayload = new("test.note", """{"text":"hi"}"""u8.ToArray());

    internal static BrainEntry EntryFixture() => new(
        7, "neuron.host/b1/chat/p", "state", BrainRecordChange.Write, """{"title":"m","count":1,"tags":[]}"""u8.ToArray(),
        "b1/chat/p", [NotePayload], 3, 11, 1_792_886_400_000);

    internal static BrainSnapshot SnapshotFixture() => new(
        7, 11,
        new(StringComparer.Ordinal) { ["neuron.host/b1/chat/p"] = new(StringComparer.Ordinal) { ["state"] = new("""{"n":1}"""u8.ToArray(), 7) } },
        [EntryFixture() with { GrainId = "", StateName = "", Record = [], Change = BrainRecordChange.None }],
        [new PublisherMark("b1/chat/p", 2, 3)]);

    [Fact]
    public void The_segment_header_is_DBLG_and_format_Json()
    {
        Assert.Equal(2, (byte)BrainLogFormat.Json);
        Assert.Equal(SegmentHeader, Convert.ToHexString([.. "DBLG"u8, (byte)BrainLogFormat.Json]));
    }

    [Fact]
    public void A_BrainEntry_with_a_json_signal_payload_has_pinned_bytes()
    {
        var serializer = Serializer();
        Assert.Equal(Entry, Convert.ToHexString(serializer.SerializeToArray(EntryFixture())));
        var read = serializer.Deserialize<BrainEntry>(Convert.FromHexString(Entry))!;
        Assert.Equal("test.note", read.Signals[0].Type);
        Assert.Equal(NotePayload.Json, read.Signals[0].Json);
        Assert.Equal((7L, 3L, 11L, 1_792_886_400_000L), (read.Sequence, read.FirstSignal, read.FirstPosition, read.AtUnixMs));
    }

    [Fact]
    public void A_BrainSnapshot_with_publisher_floors_has_pinned_bytes()
    {
        var serializer = Serializer();
        Assert.Equal(Snapshot, Convert.ToHexString(serializer.SerializeToArray(SnapshotFixture())));
        var read = serializer.Deserialize<BrainSnapshot>(Convert.FromHexString(Snapshot))!;
        Assert.Equal(new PublisherMark("b1/chat/p", 2, 3), read.Publishers[0]);
        Assert.Equal(7, read.Records["neuron.host/b1/chat/p"]["state"].ETag);
        Assert.Equal("b1/chat/p", read.Signals[0].Publisher);
    }

    [Fact]
    public void A_BrainEntry_carrying_the_subscriber_cursor_has_pinned_bytes()
    {
        var serializer = Serializer();
        var fixture = EntryFixture() with { SideStateName = "synapses", SideRecord = """{"Sources":[]}"""u8.ToArray() };
        Assert.Equal(EntryWithCursor, Convert.ToHexString(serializer.SerializeToArray(fixture)));
        var read = serializer.Deserialize<BrainEntry>(Convert.FromHexString(EntryWithCursor))!;
        Assert.Equal("synapses", read.SideStateName);
        Assert.Equal(fixture.SideRecord, read.SideRecord);
    }

    [Fact]
    public void Entries_and_snapshots_written_before_the_side_state_still_read()
    {
        var serializer = Serializer();
        var entry = serializer.Deserialize<BrainEntry>(Convert.FromHexString(EntryBeforeSideState))!;
        Assert.Equal((7L, "state", "test.note"), (entry.Sequence, entry.StateName, entry.Signals[0].Type));
        Assert.Null(entry.SideStateName);
        Assert.Null(entry.SideRecord);
        var snapshot = serializer.Deserialize<BrainSnapshot>(Convert.FromHexString(SnapshotBeforeSideState))!;
        Assert.Equal(7, snapshot.Records["neuron.host/b1/chat/p"]["state"].ETag);
        Assert.Null(snapshot.Signals[0].SideStateName);
    }

    [Fact]
    public void A_BrainEntry_carrying_an_opaque_record_and_its_state_type_has_pinned_bytes()
    {
        var serializer = Serializer();
        var fixture = EntryFixture() with { Record = [0x01, 0x02, 0xFF], RecordType = "IntoChat.OS.Legacy.ChatState" };
        Assert.Equal(EntryWithRecordType, Convert.ToHexString(serializer.SerializeToArray(fixture)));
        var read = serializer.Deserialize<BrainEntry>(Convert.FromHexString(EntryWithRecordType))!;
        Assert.Equal("IntoChat.OS.Legacy.ChatState", read.RecordType);
        Assert.Equal(fixture.Record, read.Record);
    }

    [Fact]
    public void Entries_and_snapshots_written_before_the_record_type_still_read()
    {
        var serializer = Serializer();
        var entry = serializer.Deserialize<BrainEntry>(Convert.FromHexString(EntryBeforeRecordType))!;
        Assert.Equal((7L, "state", "test.note"), (entry.Sequence, entry.StateName, entry.Signals[0].Type));
        Assert.Null(entry.RecordType);
        var cursor = serializer.Deserialize<BrainEntry>(Convert.FromHexString(EntryWithCursorBeforeRecordType))!;
        Assert.Equal("synapses", cursor.SideStateName);
        Assert.Null(cursor.RecordType);
        var snapshot = serializer.Deserialize<BrainSnapshot>(Convert.FromHexString(SnapshotBeforeRecordType))!;
        Assert.Equal(7, snapshot.Records["neuron.host/b1/chat/p"]["state"].ETag);
        Assert.Null(snapshot.Records["neuron.host/b1/chat/p"]["state"].Type);
    }
}
