namespace DigitalBrain.Apps;

public static class AppCalls
{
    public static async Task<string> Ask(this IApp app, string operation, string input = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invocation = await app.Invoke(new(Guid.NewGuid(), operation, input));
        return await app.AwaitResponse(invocation.Id, cancellationToken);
    }

    public static async Task<string> AwaitResponse(this IApp app, Guid invocationId, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            while (true)
            {
                var invocation = await app.ReadInvocation(invocationId).WaitAsync(deadline.Token);
                if (invocation.Status == InvocationStatus.Failed) { throw new InvalidOperationException(invocation.Error ?? "The app failed."); }
                if (invocation.Status == InvocationStatus.Completed) { return invocation.Output ?? ""; }
                await Task.Delay(200, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("The app did not answer within 5 minutes."); }
    }
}
