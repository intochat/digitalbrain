using System.Text;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage.Tests.Unit;

public sealed class StoragePathResolutionFacts
{
    private sealed class FakeMemoryStorage : IStorage
    {
        private readonly List<Item> _roots = [];
        private readonly Dictionary<string, List<Item>> _folderContents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _fileData = new(StringComparer.OrdinalIgnoreCase);

        public void AddRoot(Item item) => _roots.Add(item);

        public void AddChild(ItemId folderId, Item item, byte[]? content = null)
        {
            if (!_folderContents.TryGetValue(folderId.Value, out var list))
            {
                list = [];
                _folderContents[folderId.Value] = list;
            }
            list.Add(item);
            if (content is not null)
            {
                _fileData[item.Id.Value] = content;
            }
        }

        public Task<StorageDescriptor> Describe(CancellationToken ct = default)
            => Task.FromResult(new StorageDescriptor("test", "test", Quota.Unlimited(0), ItemFlags.None));

        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Item>>(_roots);

        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default)
        {
            var items = _folderContents.TryGetValue(folder.Value, out var list) ? list : [];
            return Task.FromResult(new Page<Item>(items, null));
        }

        public Task<Item> Stat(ItemId id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default)
        {
            if (_fileData.TryGetValue(id.Value, out var bytes))
            {
                return Task.FromResult(new Content(bytes, "text/plain", bytes.Length));
            }
            throw new System.IO.FileNotFoundException();
        }

        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task ResolvePath_WithEmptyPath_ReturnsRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new FakeMemoryStorage();
        var root = new Item(new ItemId("root-1"), "root", ItemKind.Folder, null, null, ItemFlags.None);
        storage.AddRoot(root);

        var resolved = await storage.ResolvePath("", ct);
        Assert.NotNull(resolved);
        Assert.Equal("root-1", resolved.Id.Value);
    }

    [Fact]
    public async Task ResolvePath_TraversesNestedHierarchy_CaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new FakeMemoryStorage();
        var root = new Item(new ItemId("root-1"), "workspace", ItemKind.Folder, null, null, ItemFlags.None);
        storage.AddRoot(root);

        var docs = new Item(new ItemId("folder-docs"), "docs", ItemKind.Folder, null, null, ItemFlags.None);
        storage.AddChild(root.Id, docs);

        var readme = new Item(new ItemId("file-readme"), "README.md", ItemKind.File, 12, null, ItemFlags.None);
        storage.AddChild(docs.Id, readme, Encoding.UTF8.GetBytes("Hello World!"));

        var found = await storage.ResolvePath("workspace/Docs/readme.md", ct);
        Assert.NotNull(found);
        Assert.Equal("file-readme", found.Id.Value);
        Assert.Equal(ItemKind.File, found.Kind);

        var text = await storage.ReadTextByPath("workspace/Docs/readme.md", ct);
        Assert.Equal("Hello World!", text);
    }

    [Fact]
    public async Task ResolvePath_SingleRootStorage_ResolvesRelativeToRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new FakeMemoryStorage();
        var root = new Item(new ItemId("root-1"), "drive", ItemKind.Folder, null, null, ItemFlags.None);
        storage.AddRoot(root);

        var notes = new Item(new ItemId("file-notes"), "notes.txt", ItemKind.File, 5, null, ItemFlags.None);
        storage.AddChild(root.Id, notes, Encoding.UTF8.GetBytes("Notes"));

        // Path does not include 'drive'
        var found = await storage.ResolvePath("notes.txt", ct);
        Assert.NotNull(found);
        Assert.Equal("file-notes", found.Id.Value);

        var text = await storage.ReadTextByPath("notes.txt", ct);
        Assert.Equal("Notes", text);
    }

    [Fact]
    public async Task ResolvePath_ReturnsNull_WhenNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new FakeMemoryStorage();
        var root = new Item(new ItemId("root-1"), "workspace", ItemKind.Folder, null, null, ItemFlags.None);
        storage.AddRoot(root);

        var found = await storage.ResolvePath("workspace/nonexistent.txt", ct);
        Assert.Null(found);

        await Assert.ThrowsAsync<System.IO.FileNotFoundException>(() => storage.ReadTextByPath("workspace/nonexistent.txt", ct));
    }
}
