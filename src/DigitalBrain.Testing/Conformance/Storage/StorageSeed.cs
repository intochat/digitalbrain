using System.Text;
using DigitalBrain.Capabilities.Storage;

namespace DigitalBrain.Testing.Conformance.Storage;

/// <summary>One item of the <see cref="StorageSeed"/> tree.</summary>
/// <param name="Path">The path below the seed folder, <c>/</c>-separated (for example <c>Notes/a.txt</c>).</param>
/// <param name="Kind">File or folder.</param>
/// <param name="Bytes">The file content; empty for folders.</param>
public sealed record SeedEntry(string Path, ItemKind Kind, ReadOnlyMemory<byte> Bytes);

/// <summary>
/// The canonical tree a <see cref="StorageConformance{TStorage}"/> subclass creates in its backing store before the kit runs:
/// <c>Notes/a.txt</c> ("hello kit"), <c>Plan.txt</c> (bytes 0..15), <c>Deep/1/2/3/leaf.txt</c> and <c>Many/</c> with 250 files
/// <c>f000.txt</c>..<c>f249.txt</c>. Exposed as data so stub servers can serve it. Folders come before their contents.
/// </summary>
public sealed class StorageSeed
{
    /// <summary>The name of the note folder.</summary>
    public const string NotesFolder = "Notes";

    /// <summary>The name of the UTF-8 note inside <see cref="NotesFolder"/>.</summary>
    public const string NoteName = "a.txt";

    /// <summary>The text of the note.</summary>
    public const string NoteText = "hello kit";

    /// <summary>The name of the 16-byte file at the top of the seed folder.</summary>
    public const string PlanName = "Plan.txt";

    /// <summary>The folder holding <see cref="ManyCount"/> files.</summary>
    public const string ManyFolder = "Many";

    /// <summary>How many files <see cref="ManyFolder"/> holds.</summary>
    public const int ManyCount = 250;

    /// <summary>The folder chain leading to <see cref="LeafName"/>.</summary>
    public static IReadOnlyList<string> DeepChain { get; } = ["Deep", "1", "2", "3"];

    /// <summary>The deepest seeded file.</summary>
    public const string LeafName = "leaf.txt";

    private StorageSeed(IReadOnlyList<SeedEntry> entries) => Entries = entries;

    /// <summary>The canonical seed.</summary>
    public static StorageSeed Canonical { get; } = Build();

    /// <summary>The bytes of <see cref="PlanName"/>: 0, 1, ..., 15.</summary>
    public static ReadOnlyMemory<byte> PlanBytes { get; } = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();

    /// <summary>Every item, folders before their contents.</summary>
    public IReadOnlyList<SeedEntry> Entries { get; }

    /// <summary>The name of the many-folder file at <paramref name="index"/> (<c>f000.txt</c>..).</summary>
    public static string ManyName(int index) => $"f{index:D3}.txt";

    private static StorageSeed Build()
    {
        var entries = new List<SeedEntry>
        {
            Folder(NotesFolder),
            File($"{NotesFolder}/{NoteName}", Encoding.UTF8.GetBytes(NoteText)),
            File(PlanName, Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()),
        };
        var path = string.Empty;
        foreach (var part in DeepChain)
        {
            path = path.Length == 0 ? part : $"{path}/{part}";
            entries.Add(Folder(path));
        }
        entries.Add(File($"{path}/{LeafName}", "leaf"u8.ToArray()));
        entries.Add(Folder(ManyFolder));
        for (var index = 0; index < ManyCount; index++)
        {
            entries.Add(File($"{ManyFolder}/{ManyName(index)}", Encoding.UTF8.GetBytes(index.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        return new StorageSeed(entries);
    }

    private static SeedEntry Folder(string path) => new(path, ItemKind.Folder, ReadOnlyMemory<byte>.Empty);

    private static SeedEntry File(string path, byte[] bytes) => new(path, ItemKind.File, bytes);
}
