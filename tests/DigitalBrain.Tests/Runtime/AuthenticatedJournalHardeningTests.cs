using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class AuthenticatedJournalHardeningTests
{
    private const string Domain = "digitalbrain.tests.authenticated-journal";

    [Fact]
    public void Missing_head_never_downgrades_an_authenticated_journal_but_plaintext_migrates_once()
    {
        var root = TemporaryRoot("head");
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var authenticatedPath = Path.Combine(root, "authenticated.jsonl");
            var authenticated = Open(authenticatedPath, key);
            authenticated.Append("record", "{\"value\":1}");
            File.Delete(authenticatedPath + ".head");
            var lengthBeforeReopen = new FileInfo(authenticatedPath).Length;

            Assert.Throws<InvalidDataException>(() =>
                new AuthenticatedJsonLinesJournal(Domain, key, authenticatedPath).Read());
            Assert.Equal(lengthBeforeReopen, new FileInfo(authenticatedPath).Length);

            var legacyPath = Path.Combine(root, "legacy.jsonl");
            File.WriteAllText(legacyPath, "{\"legacy\":true}" + Environment.NewLine);
            var migrating = new AuthenticatedJsonLinesJournal(Domain, key, legacyPath);
            Assert.Single(migrating.Read());
            migrating.SealLegacy();
            var migratedLength = new FileInfo(legacyPath).Length;
            Assert.True(File.Exists(legacyPath + ".head"));

            var reopened = new AuthenticatedJsonLinesJournal(Domain, key, legacyPath);
            Assert.Single(reopened.Read());
            reopened.SealLegacy();
            Assert.Equal(migratedLength, new FileInfo(legacyPath).Length);

            File.Delete(legacyPath + ".head");
            Assert.Throws<InvalidDataException>(() =>
                new AuthenticatedJsonLinesJournal(Domain, key, legacyPath).Read());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Multiple_instances_refresh_the_chain_under_exclusive_writer_lock()
    {
        var root = TemporaryRoot("writers");
        try
        {
            var path = Path.Combine(root, "journal.jsonl");
            var key = RandomNumberGenerator.GetBytes(32);
            var journals = Enumerable.Range(0, 16)
                .Select(_ => Open(path, key))
                .ToArray();

            await Task.WhenAll(journals.Select((journal, index) => Task.Run(() =>
                journal.Append("record." + index, JsonSerializer.Serialize(new { index })))).ToArray());

            var recovered = new AuthenticatedJsonLinesJournal(Domain, key, path).Read();
            Assert.Equal(16, recovered.Count);
            Assert.Equal(16, recovered.Select(static item => item.Kind).Distinct(StringComparer.Ordinal).Count());
            Assert.False(File.Exists(path + ".pending"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Head_write_failure_poisons_the_instance_and_reopen_finishes_the_authenticated_commit()
    {
        var root = TemporaryRoot("head-failure");
        try
        {
            var path = Path.Combine(root, "journal.jsonl");
            var key = RandomNumberGenerator.GetBytes(32);
            var journal = Open(path, key, new AuthenticatedJournalFaultInjection(
                BeforeHeadWrite: () => throw new IOException("injected head failure")));

            Assert.Throws<InvalidDataException>(() => journal.Append("record", "{\"value\":1}"));
            Assert.True(File.Exists(path + ".pending"));
            Assert.False(File.Exists(path + ".head"));
            Assert.Throws<InvalidOperationException>(() => journal.Append("second", "{}"));

            var recovered = new AuthenticatedJsonLinesJournal(Domain, key, path).Read();
            Assert.Equal("record", Assert.Single(recovered).Kind);
            Assert.True(File.Exists(path + ".head"));
            Assert.False(File.Exists(path + ".pending"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Pending_witness_repairs_a_torn_physical_append_without_accepting_the_record()
    {
        var root = TemporaryRoot("pending-tail");
        try
        {
            var path = Path.Combine(root, "journal.jsonl");
            var key = RandomNumberGenerator.GetBytes(32);
            var journal = Open(path, key, new AuthenticatedJournalFaultInjection(
                BeforeHeadWrite: () => throw new IOException("injected head failure")));
            Assert.Throws<InvalidDataException>(() => journal.Append("record", "{\"private\":\"never-copy\"}"));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
                stream.SetLength(Math.Max(1, stream.Length / 2));
            var truncatedLength = new FileInfo(path).Length;

            var recovered = new AuthenticatedJsonLinesJournal(Domain, key, path).Read();

            Assert.Empty(recovered);
            Assert.Equal(0, new FileInfo(path).Length);
            Assert.True(truncatedLength > 0);
            Assert.False(File.Exists(path + ".pending"));
            var quarantine = File.ReadAllText(path + ".quarantine");
            Assert.Contains("incomplete-pending-append", quarantine, StringComparison.Ordinal);
            Assert.DoesNotContain("never-copy", quarantine, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Truncated_anchored_record_fails_without_repair_but_new_torn_suffix_is_repaired_after_anchor_validation()
    {
        var root = TemporaryRoot("tails");
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var attackedPath = Path.Combine(root, "attacked.jsonl");
            var attacked = Open(attackedPath, key);
            attacked.Append("one", "{\"value\":1}");
            attacked.Append("two", "{\"value\":2}");
            using (var stream = new FileStream(attackedPath, FileMode.Open, FileAccess.Write, FileShare.Read))
                stream.SetLength(stream.Length - 20);
            var maliciousLength = new FileInfo(attackedPath).Length;

            Assert.Throws<InvalidDataException>(() =>
                new AuthenticatedJsonLinesJournal(Domain, key, attackedPath).Read());
            Assert.Equal(maliciousLength, new FileInfo(attackedPath).Length);

            var recoverablePath = Path.Combine(root, "recoverable.jsonl");
            var recoverable = Open(recoverablePath, key);
            recoverable.Append("one", "{\"value\":1}");
            var anchoredLength = new FileInfo(recoverablePath).Length;
            const string privateTail = "{\"private-tail\":\"never-copy\"";
            File.AppendAllText(recoverablePath, privateTail);

            var reopened = new AuthenticatedJsonLinesJournal(Domain, key, recoverablePath).Read();
            Assert.Single(reopened);
            Assert.Equal(anchoredLength, new FileInfo(recoverablePath).Length);
            var quarantine = File.ReadAllText(recoverablePath + ".quarantine");
            Assert.Contains("incomplete-final-append", quarantine, StringComparison.Ordinal);
            Assert.DoesNotContain("private-tail", quarantine, StringComparison.Ordinal);
            Assert.DoesNotContain("never-copy", quarantine, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Missing_head_with_an_incomplete_suffix_fails_without_truncating_any_bytes()
    {
        var root = TemporaryRoot("headless-tail");
        try
        {
            var path = Path.Combine(root, "journal.jsonl");
            var key = RandomNumberGenerator.GetBytes(32);
            var journal = Open(path, key);
            journal.Append("record", "{}");
            File.Delete(path + ".head");
            File.AppendAllText(path, "{\"truncated\":");
            var length = new FileInfo(path).Length;

            Assert.Throws<InvalidDataException>(() =>
                new AuthenticatedJsonLinesJournal(Domain, key, path).Read());
            Assert.Equal(length, new FileInfo(path).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Public_durable_store_constructors_expose_no_arbitrary_append_sink()
    {
        var journalConstructor = Assert.Single(typeof(AuthenticatedJsonLinesJournal).GetConstructors());
        Assert.Equal(
            [typeof(string), typeof(byte[]), typeof(string)],
            journalConstructor.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.DoesNotContain(typeof(ApplicationService).GetConstructors(), constructor =>
            constructor.GetParameters().Any(static parameter => parameter.ParameterType == typeof(Action<string>)));
        Assert.DoesNotContain(typeof(FileSessionManager).GetConstructors(), constructor =>
            constructor.GetParameters().Any(static parameter => parameter.ParameterType == typeof(Action<string>)));
    }

    private static AuthenticatedJsonLinesJournal Open(
        string path,
        byte[] key,
        AuthenticatedJournalFaultInjection? faultInjection = null)
    {
        var journal = new AuthenticatedJsonLinesJournal(Domain, key, path, faultInjection);
        journal.Read();
        return journal;
    }

    private static string TemporaryRoot(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), "authenticated-journal-" + suffix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
