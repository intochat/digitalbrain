namespace DigitalBrain.Invocation;

// A neuron queued work that will signal this brain after its caller has returned. The runtime
// keeps the turn open across that gap so a projection is not published once per script.
public interface IWorkContinuation
{
    Task Acquire(string brainScope, string token);

    Task Release(string brainScope, string token);
}
