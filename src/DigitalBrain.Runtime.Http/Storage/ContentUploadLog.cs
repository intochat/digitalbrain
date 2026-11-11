using DigitalBrain.Operations;

namespace DigitalBrain.Runtime.Http.Storage;

/// <summary>Upload failures the gateway reports as <see cref="OperationDone"/>.</summary>
public sealed class ContentUploadLog
{
    private readonly List<OperationDone> _done = [];

    public IReadOnlyList<OperationDone> Done
    {
        get { lock (_done) { return [.. _done]; } }
    }

    public void Failed(OperationId id)
    {
        lock (_done) { _done.Add(new OperationDone(id, new OperationResult.Failed("aborted", "The upload was aborted."))); }
    }
}
