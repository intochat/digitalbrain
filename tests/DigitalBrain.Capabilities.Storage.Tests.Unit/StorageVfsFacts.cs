using System.Text;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage.Tests.Unit;

public sealed class StorageVfsFacts
{
    private sealed class FakeStorage : IStorage
    {
        private readonly List<Item> _roots = [];
        private readonly Dictionary<string, List<Item>> _folderContents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _fileData = new(StringComparer.OrdinalIgnoreCase);

        public List<WritePlan> ExecutedWrites { get; } = [];

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
            => Task.FromResult(new StorageDescriptor("fake", "Fake Storage", Quota.Unlimited(0), ItemFlags.None));

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
        {
            ExecutedWrites.Add(plan);
            return Task.FromResult(new OperationId(Guid.NewGuid().ToString("D")));
        }

        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default)
        {
            if (_fileData.TryGetValue(item.Value, out var bytes))
            {
                var stream = new System.IO.MemoryStream(bytes);
                return Task.FromResult(new StorageReadStream(stream, bytes.Length, "text/plain", null, canSeek: true, DateTimeOffset.UtcNow));
            }
            throw new System.IO.FileNotFoundException();
        }

        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task GetMounts_ReturnsConfiguredMounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var driveStorage = new FakeStorage();
        var localDiskStorage = new FakeStorage();

        var vfs = new StorageVfs([
            new VfsMount("drive", "Google Drive", driveStorage),
            new VfsMount("local", "Local Disk", localDiskStorage),
        ]);

        var mounts = await vfs.GetMounts(ct);
        Assert.Equal(2, mounts.Count);
        Assert.Contains(mounts, m => m.MountPoint == "drive" && m.Label == "Google Drive");
        Assert.Contains(mounts, m => m.MountPoint == "local" && m.Label == "Local Disk");
    }

    [Fact]
    public async Task List_EmptyPath_ReturnsRootMountsAsFolders()
    {
        var ct = TestContext.Current.CancellationToken;
        var driveStorage = new FakeStorage();
        var localDiskStorage = new FakeStorage();

        var vfs = new StorageVfs([
            new VfsMount("drive", "Google Drive", driveStorage),
            new VfsMount("local", "Local Disk", localDiskStorage),
        ]);

        var items = await vfs.List("", ct);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Path == "/drive" && i.Kind == ItemKind.Folder);
        Assert.Contains(items, i => i.Path == "/local" && i.Kind == ItemKind.Folder);
    }

    [Fact]
    public async Task ReadText_TraversesMountAndResolvesFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var drive = new FakeStorage();
        var root = new Item(new ItemId("drive-root"), "root", ItemKind.Folder, null, null, ItemFlags.None);
        drive.AddRoot(root);

        var docs = new Item(new ItemId("folder-docs"), "docs", ItemKind.Folder, null, null, ItemFlags.None);
        drive.AddChild(root.Id, docs);

        var report = new Item(new ItemId("file-report"), "report.txt", ItemKind.File, 12, null, ItemFlags.None);
        drive.AddChild(docs.Id, report, Encoding.UTF8.GetBytes("Virtual filesystem content"));

        var vfs = new StorageVfs([new VfsMount("drive", "Google Drive", drive)]);

        var text = await vfs.ReadText("/drive/docs/report.txt", ct);
        Assert.Equal("Virtual filesystem content", text);

        var stat = await vfs.Stat("/drive/docs/report.txt", ct);
        Assert.NotNull(stat);
        Assert.Equal("report.txt", stat.Name);
        Assert.Equal(ItemKind.File, stat.Kind);
        Assert.Equal("/drive/docs/report.txt", stat.Path);
    }

    [Fact]
    public async Task Write_DelegatesToMountWithResolvedTargetFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        var drive = new FakeStorage();
        var root = new Item(new ItemId("drive-root"), "root", ItemKind.Folder, null, null, ItemFlags.None);
        drive.AddRoot(root);

        var docs = new Item(new ItemId("folder-docs"), "docs", ItemKind.Folder, null, null, ItemFlags.None);
        drive.AddChild(root.Id, docs);

        var vfs = new StorageVfs([new VfsMount("drive", "Google Drive", drive)]);

        var bytes = Encoding.UTF8.GetBytes("New file bytes");
        var opId = await vfs.Write("/drive/docs/new.txt", bytes, ct);
        Assert.False(string.IsNullOrEmpty(opId.Value));

        Assert.Single(drive.ExecutedWrites);
        var write = drive.ExecutedWrites[0];
        Assert.Equal("docs", write.TargetFolder?.Value == docs.Id.Value ? "docs" : "wrong");
        Assert.Equal("new.txt", write.Name);
        Assert.True(write.Inline.HasValue && write.Inline.Value.Span.SequenceEqual(bytes));
    }

    [Fact]
    public async Task Accessing_UnknownMount_ThrowsDirectoryNotFoundException()
    {
        var ct = TestContext.Current.CancellationToken;
        var vfs = new StorageVfs([]);

        await Assert.ThrowsAsync<System.IO.DirectoryNotFoundException>(() => vfs.List("/nonexistent", ct));
        await Assert.ThrowsAsync<System.IO.DirectoryNotFoundException>(() => vfs.ReadText("/nonexistent/file.txt", ct));
    }
}
