namespace DigitalBrain.Operations;

/// <summary>Helpers for awaiting operations.</summary>
public static class OperationWait
{
    /// <summary>Polls until the operation is complete and returns the terminal <see cref="OperationView"/>.</summary>
    public static async Task<OperationView> For(IOperationsQuery query, OperationId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        while (!ct.IsCancellationRequested)
        {
            var view = await query.Read(id, ct).ConfigureAwait(false);
            if (view is { Result: not null })
            {
                return view;
            }

            await Task.Delay(25, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException();
    }
}
