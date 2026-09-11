using Xunit;

namespace DigitalBrain.Tests;

internal static class UiWait
{
    internal static async Task Until(Func<Task<bool>> ready, string message = "The UI reaction did not finish within 10 seconds.")
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await ready())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail(message);
    }
}
