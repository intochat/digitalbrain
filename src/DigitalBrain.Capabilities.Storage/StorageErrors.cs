using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Capabilities.Storage;

// The error vocabulary is fixed by the contract: short names, a stable Code, the item id only (never content or
// credentials in a message). The standard exception constructors would allow messages without a code.

/// <summary>A storage failure with a stable <see cref="Code"/>. Messages carry the code and item id only.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public abstract class StorageException : Exception
{
    /// <summary>Creates an error.</summary>
    protected StorageException(string code, ItemId? item, Exception? innerException)
        : base(item is { } id ? $"storage {code}: {id.Value}" : $"storage {code}", innerException)
    {
        Code = code;
        Item = item;
    }

    /// <summary>The stable error code, for example <c>not-found</c>.</summary>
    public string Code { get; }

    /// <summary>The item concerned, when there is one.</summary>
    public ItemId? Item { get; }
}

/// <summary>The item does not exist. Code <c>not-found</c>.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract vocabulary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public sealed class NotFound(ItemId? item, Exception? innerException = null) : StorageException("not-found", item, innerException);

/// <summary>The target name exists and the conflict policy is fail. Code <c>conflict</c>.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract vocabulary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public sealed class Conflict(ItemId? item, Exception? innerException = null) : StorageException("conflict", item, innerException);

/// <summary>The caller may not do this, or the id escapes the storage's confinement. Code <c>denied</c>.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract vocabulary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public sealed class Denied(ItemId? item, Exception? innerException = null) : StorageException("denied", item, innerException);

/// <summary>The storage is full. Code <c>quota-exceeded</c>.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract vocabulary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public sealed class QuotaExceeded(ItemId? item, Exception? innerException = null) : StorageException("quota-exceeded", item, innerException);

/// <summary>The storage cannot be reached now; retry later. Code <c>unavailable</c>.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract vocabulary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public sealed class Unavailable(ItemId? item, Exception? innerException = null) : StorageException("unavailable", item, innerException);

/// <summary>The storage connection needs the owner to sign in again. Code <c>needs-auth</c>.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract vocabulary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public sealed class NeedsAuth(ItemId? item, Exception? innerException = null) : StorageException("needs-auth", item, innerException);

/// <summary>The storage does not support this action. Code <c>unsupported</c>.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract vocabulary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every error carries a code.")]
public sealed class Unsupported(ItemId? item, Exception? innerException = null) : StorageException("unsupported", item, innerException);
