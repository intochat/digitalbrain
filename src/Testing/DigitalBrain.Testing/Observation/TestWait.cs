namespace DigitalBrain.Testing;

public static class TestWait
{
    public static async Task<T> UntilAsync<T>(Func<CancellationToken, Task<T>> read, Func<T, bool> done,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(done);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        T? last = default;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                last = await read(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                if (done(last)) { return last; }
                await Task.Delay(TestLimits.Poll, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var summary = last is null ? "none" : typeof(T).IsPrimitive || typeof(T).IsEnum ? last.ToString() : typeof(T).Name + " (payload omitted)";
            throw new TimeoutException($"Condition not satisfied within {timeout}; last observation: {summary}.");
        }
    }
}
