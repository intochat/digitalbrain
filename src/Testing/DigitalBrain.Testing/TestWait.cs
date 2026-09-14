namespace DigitalBrain.Testing;

// One wait for every "read until it says what I expect" loop: a brain answers commands by saving a new
// snapshot later, so a fact reads the snapshot until it carries the settle it is about. The timeout names
// the last value it saw, which is what a failure needs to be readable without a debugger.
public static class TestWait
{
    public static async Task<T> UntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(done);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        T last = default!;
        while (true)
        {
            last = await read().ConfigureAwait(false);
            if (done(last))
            {
                return last;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Nothing satisfied the condition within {timeout.TotalSeconds:0}s; the last value was {last?.ToString() ?? "null"}.");
            }
        }
    }
}
