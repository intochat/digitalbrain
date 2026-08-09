namespace DigitalBrain.Poc.Runtime.Tests;

internal static class TestPocRoot
{
    public static string Find()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "poc", "DigitalBrain.Poc.slnx");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the POC root.");
    }
}
