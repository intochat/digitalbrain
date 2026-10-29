using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>What a write does.</summary>
public enum WriteKind
{
    /// <summary>Create a folder named <see cref="WritePlan.Name"/> in <see cref="WritePlan.TargetFolder"/>.</summary>
    CreateFolder,

    /// <summary>Write a file named <see cref="WritePlan.Name"/> in <see cref="WritePlan.TargetFolder"/>.</summary>
    WriteFile,

    /// <summary>Move <see cref="WritePlan.Source"/> into <see cref="WritePlan.TargetFolder"/>.</summary>
    Move,

    /// <summary>Copy <see cref="WritePlan.Source"/> into <see cref="WritePlan.TargetFolder"/>.</summary>
    Copy,

    /// <summary>Rename <see cref="WritePlan.Source"/> to <see cref="WritePlan.Name"/>.</summary>
    Rename,

    /// <summary>Delete <see cref="WritePlan.Source"/>: to the trash, or permanently when <see cref="WritePlan.Permanent"/>.</summary>
    Delete,
}

/// <summary>What happens when the target name already exists.</summary>
public enum ConflictPolicy
{
    /// <summary>Fail with <see cref="Conflict"/>. The default.</summary>
    Fail,

    /// <summary>Pick a free name.</summary>
    Rename,

    /// <summary>Replace the existing item. Only by explicit choice.</summary>
    Overwrite,
}

/// <summary>
/// A storage change. Build it with the factories; every factory defaults <see cref="Conflict"/> to
/// <see cref="ConflictPolicy.Fail"/> and delete defaults to the trash.
/// </summary>
/// <param name="Kind">What the write does.</param>
/// <param name="Source">The item acted on (move, copy, rename, delete).</param>
/// <param name="TargetFolder">The destination folder (create folder, write file, move, copy).</param>
/// <param name="Name">The new name (create folder, write file, rename).</param>
/// <param name="Conflict">What happens when the target name exists.</param>
/// <param name="Permanent">For delete: true deletes permanently, false moves to the trash.</param>
/// <param name="Inline">For write file: small content carried in the plan; large content goes through <see cref="IStorage.OpenWrite"/>.</param>
/// <param name="DryRun">When true the operation only plans.</param>
public sealed record WritePlan(
    WriteKind Kind,
    ItemId? Source,
    ItemId? TargetFolder,
    string? Name,
    ConflictPolicy Conflict = ConflictPolicy.Fail,
    bool Permanent = false,
    ReadOnlyMemory<byte>? Inline = null,
    bool DryRun = false) : OperationPlan(DryRun)
{
    /// <summary>Create a folder.</summary>
    public static WritePlan CreateFolder(ItemId parent, string name, ConflictPolicy conflict = ConflictPolicy.Fail) =>
        new(WriteKind.CreateFolder, null, parent, Named(name), conflict);

    /// <summary>Write a file, with inline content or (null) content streamed through <see cref="IStorage.OpenWrite"/>.</summary>
    public static WritePlan WriteFile(ItemId folder, string name, ReadOnlyMemory<byte>? content = null, ConflictPolicy conflict = ConflictPolicy.Fail) =>
        new(WriteKind.WriteFile, null, folder, Named(name), conflict, Inline: content);

    /// <summary>Move an item into a folder.</summary>
    public static WritePlan Move(ItemId source, ItemId folder, ConflictPolicy conflict = ConflictPolicy.Fail) =>
        new(WriteKind.Move, source, folder, null, conflict);

    /// <summary>Copy an item into a folder.</summary>
    public static WritePlan Copy(ItemId source, ItemId folder, ConflictPolicy conflict = ConflictPolicy.Fail) =>
        new(WriteKind.Copy, source, folder, null, conflict);

    /// <summary>Rename an item.</summary>
    public static WritePlan Rename(ItemId source, string name, ConflictPolicy conflict = ConflictPolicy.Fail) =>
        new(WriteKind.Rename, source, null, Named(name), conflict);

    /// <summary>Delete an item: to the trash unless <paramref name="permanent"/>.</summary>
    public static WritePlan Delete(ItemId source, bool permanent = false) =>
        new(WriteKind.Delete, source, null, null, ConflictPolicy.Fail, permanent);

    private static string Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
