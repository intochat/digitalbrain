using Brain.Abstractions.Capabilities;

namespace Brain.Testing.Fakes;

public sealed record ProofCapabilityInput(string Value);

public sealed record ProofCapabilityResult(string Classification);

public sealed class DeterministicCapability : ICapability<ProofCapabilityInput, ProofCapabilityResult>
{
    private readonly TaskCompletionSource<bool> _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool>? _release;
    private int _failuresRemaining;

    public int CallCount { get; private set; }

    public void FailNextInvocation() => _failuresRemaining++;

    public void BlockNextInvocation() => _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilCalledAsync() => _called.Task;

    public void ReleaseBlockedInvocation() => _release?.TrySetResult(true);

    public async Task<ProofCapabilityResult> InvokeAsync(
        ProofCapabilityInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        _called.TrySetResult(true);

        if (_failuresRemaining > 0)
        {
            _failuresRemaining--;
            throw new InvalidOperationException("The deterministic capability failed.");
        }

        if (_release is { } release)
        {
            await release.Task.WaitAsync(cancellationToken);
            _release = null;
        }

        return new ProofCapabilityResult("classified/" + input.Value);
    }
}
