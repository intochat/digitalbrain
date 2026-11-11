namespace DigitalBrain.Capabilities.Storage;

/// <summary>Resolves the storage connection owner for a brain and origin.</summary>
public interface IStorageOwnerSource
{
    /// <summary>Returns a storage owner delegate for the given brain and origin.</summary>
    StorageOwner For(BrainId brain, string origin);
}
