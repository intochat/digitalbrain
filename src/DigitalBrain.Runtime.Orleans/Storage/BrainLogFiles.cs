namespace DigitalBrain.Runtime.Orleans;

// The journal's file access, so tests can fault an append on any platform.
internal interface IBrainLogFiles
{
    // Appends to the journal; readers may share it.
    Stream OpenAppend(string path);

    // Opens or creates the journal for recovery, which may truncate a torn tail.
    Stream OpenRead(string path);
}

internal sealed class FileBrainLogFiles : IBrainLogFiles
{
    public static readonly FileBrainLogFiles Instance = new();

    public Stream OpenAppend(string path) => new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);

    public Stream OpenRead(string path) => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
}

// The default ownership: an exclusive writer.lock in the brain's directory, held for the life of the log.
internal sealed class FileLockBrainLogOwnership : IBrainLogOwnership
{
    public static FileLockBrainLogOwnership Instance { get; } = new();

    public Task<IBrainLogLease> ClaimAsync(string directory, string brainId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Claim(directory, brainId));
    }

    // A second writer, in this or another process, is refused.
    public static IBrainLogLease Claim(string directory, string brainId)
    {
        Directory.CreateDirectory(directory);
        try { return new FileLease(new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException error) when (error is not FileNotFoundException and not DirectoryNotFoundException)
        { throw new BrainNotOwnedException(brainId, error); }
    }

    private sealed class FileLease(FileStream stream) : IBrainLogLease
    {
        public bool IsHeld => true;
        public long BeginWrite() => 0;
        public void EndWrite(long started) { }
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}

// Reports the configured ownership's counters, or the writer lock's none.
internal sealed class BrainLogOwnershipCounters(IBrainLogOwnership ownership) : IBrainOwnershipCounters
{
    private static readonly BrainOwnershipCounts WriterLock = new("writer-lock", 0, 0, 0, 0, 0, 0);
    public BrainOwnershipCounts Snapshot() => ownership is IBrainOwnershipCounters counters ? counters.Snapshot() : WriterLock;
}
