namespace DigitalBrain.Apps.Hosting;

public static class BehaviorIds
{
    public static string For(string owner, string path)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(path);
        return path.Length == 0 ? owner : owner + "/" + path;
    }
}
