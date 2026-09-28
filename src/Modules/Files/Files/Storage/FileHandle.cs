namespace DigitalBrain.Files;

internal sealed record FileHandle(string Scope, string Root, string RelativePath, long? Length = null, long? ModifiedTicks = null, long? CreatedTicks = null);
